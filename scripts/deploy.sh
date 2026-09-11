#!/usr/bin/env bash
#
# Deploys the worker to the server over SSH, replacing the by-hand sequence (ssh in, git pull,
# docker compose up -d --build, prune) that is easy to get half right from memory. This script
# adds the safety steps that sequence lacks: it backs up the database before pulling (migrations
# run unattended at worker startup, so every pull is also a schema change), refuses to proceed
# on a dirty remote tree or a failed backup, and actually checks the worker stayed up afterward
# instead of trusting a zero exit code from "up -d".
#
# Assumes an SSH host alias for the server already exists in ~/.ssh/config -- this script never
# takes a hostname or IP, because this repository is public. Run --help for the alias shape.
#
# Usage: scripts/deploy.sh [-y|--yes] [-h|--host <alias>] [--no-backup] [--help]
#
# Assumes the remote login shell is bash (true of a stock Ubuntu box, which is what this
# targets, and consistent with the git/docker tooling already expected to be there).

set -euo pipefail

# ---- configuration ------------------------------------------------------------------------

# How many pre-deploy dumps to keep in backups/ on the server. Older ones are removed after a
# successful deploy so backups/ does not grow without bound on a small VPS disk.
readonly BACKUP_RETAIN_COUNT=10

# How long, in seconds, to watch the worker container after the rebuild before declaring the
# deploy healthy. "docker compose up -d" returning 0 only means the container was created --
# a bad migration or a bad config value can crash it seconds later, and this is the window that
# catches that instead of reporting success on a container that is already gone.
readonly HEALTH_CHECK_SECONDS=30

# How often, in seconds, the health check polls during the window above.
readonly HEALTH_CHECK_POLL_SECONDS=2

# Default SSH host alias and remote checkout path. Never a real hostname, IP, or username here --
# see --help for how to point these at a real server via ~/.ssh/config.
readonly DEFAULT_SSH_HOST="assistant"
readonly DEFAULT_REMOTE_PATH="personal-ai-assistant"

# ---- flags and defaults, in override order: flag > env var > default ----------------------

ssh_host="${ASSISTANT_SSH_HOST:-$DEFAULT_SSH_HOST}"
remote_path="${ASSISTANT_REMOTE_PATH:-$DEFAULT_REMOTE_PATH}"
assume_yes=0
skip_backup=0

# Set once flags are parsed and read again throughout; declared here so a message can always
# reference it, backed or not.
backup_path=""

die() {
  echo "error: $*" >&2
  exit 1
}

print_usage() {
  echo "Usage: scripts/deploy.sh [-y|--yes] [-h|--host <alias>] [--no-backup] [--help]"
}

print_help() {
  print_usage
  cat <<'EOF'

Deploys the worker to the server: backs up the database, pulls the latest
main, rebuilds the containers, and verifies the worker is still running
before pruning old images. Refuses to proceed on a dirty remote tree or a
failed backup.

  -y, --yes            Skip the "does this look right" confirmation prompt.
  -h, --host <alias>   SSH host alias to deploy to (default: assistant, or
                        $ASSISTANT_SSH_HOST if set). Note: this -h is the
                        host flag, not help -- use --help for usage.
      --no-backup       Skip the pre-deploy database backup. Only for a
                        deploy with no migration in it (docs, config-only
                        changes) -- using it gives up the one thing that
                        lets you undo a bad migration, so the default is on.
      --help            Show this message and exit.

Connecting to the server:
  This script addresses the server by an SSH host alias, never by IP or
  hostname, because this repository is public. Define the alias once in
  ~/.ssh/config:

    Host assistant
      HostName <server-ip-or-hostname>
      User <remote-user>

  Everything after that -- key auth, port, and so on -- is whatever that
  alias already needs to work with a plain "ssh assistant".

Environment variables:
  ASSISTANT_SSH_HOST     SSH host alias (default: assistant)
  ASSISTANT_REMOTE_PATH  Remote checkout path, relative to the remote user's
                          home (default: personal-ai-assistant)
EOF
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      -y|--yes)
        assume_yes=1
        shift
        ;;
      -h|--host)
        if [[ $# -lt 2 ]]; then
          echo "error: $1 requires an argument" >&2
          print_usage >&2
          exit 1
        fi
        ssh_host="$2"
        shift 2
        ;;
      --no-backup)
        skip_backup=1
        shift
        ;;
      --help)
        print_help
        exit 0
        ;;
      *)
        echo "error: unknown flag: $1" >&2
        print_usage >&2
        exit 1
        ;;
    esac
  done
}

# Every remote command goes through this one place: BatchMode refuses a password/passphrase
# prompt (there is no one at the keyboard mid-deploy to answer it) and ConnectTimeout fails
# fast on a genuinely unreachable host instead of hanging.
remote() {
  ssh -o BatchMode=yes -o ConnectTimeout=10 "$ssh_host" "$@"
}

# ---- 1. preflight -----------------------------------------------------------------------

preflight() {
  echo "Checking connectivity to ${ssh_host}..."
  if ! remote true; then
    die "cannot reach SSH host alias '${ssh_host}' -- check it is defined in ~/.ssh/config and the server is up (run --help for the alias shape)"
  fi

  echo "Checking the remote checkout at ${remote_path}..."
  if ! remote "test -d ${remote_path_q}"; then
    die "remote path '${remote_path}' does not exist on ${ssh_host} -- check ASSISTANT_REMOTE_PATH or the checkout location"
  fi
  if ! remote "git -C ${remote_path_q} rev-parse --is-inside-work-tree" >/dev/null; then
    die "remote path '${remote_path}' on ${ssh_host} is not a git repository"
  fi

  echo "Checking docker compose on the server..."
  # --quiet only validates and parses the compose file; it does not print the resolved config,
  # which would otherwise put POSTGRES_PASSWORD on this terminal. Output is still redirected
  # below in case a future compose version is less quiet about it than this one.
  if ! remote "cd ${remote_path_q} && docker compose config --quiet" >/dev/null 2>&1; then
    die "docker compose is not working in '${remote_path}' on ${ssh_host} -- check Docker is running and .env is present there"
  fi
}

# ---- 2. warn about unpushed local commits ------------------------------------------------

check_unpushed_commits() {
  # The server pulls from GitHub, not from this Mac -- commits that exist only on the local
  # main branch will not deploy no matter how confidently this script runs. Best-effort and
  # non-fatal: if there is no local main to compare (e.g. a shallow or unusual checkout), say
  # nothing rather than fail a deploy over a warning that cannot be computed.
  if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    return 0
  fi
  if ! git rev-parse --verify --quiet main >/dev/null; then
    return 0
  fi
  git fetch origin main --quiet 2>/dev/null || true

  local unpushed
  unpushed="$(git log --oneline origin/main..main 2>/dev/null || true)"
  if [[ -n "$unpushed" ]]; then
    echo "warning: local main has commits not on origin/main -- these will NOT deploy:" >&2
    echo "$unpushed" | sed 's/^/  /' >&2
    echo >&2
  fi
}

# ---- 3. work out what would deploy --------------------------------------------------------

compute_pending_commits() {
  if ! pending_commits="$(remote "cd ${remote_path_q} && git fetch origin main --quiet && git log --oneline HEAD..origin/main")"; then
    die "could not fetch origin/main on ${ssh_host} -- check the server's network access to GitHub"
  fi
}

# ---- 4. refuse a dirty remote working tree -------------------------------------------------

check_remote_dirty() {
  local dirty
  # --untracked-files=no on purpose: backups/ itself is untracked on the server (never
  # committed), and an untracked file is not what would make "git pull --ff-only" fail. Only a
  # modified tracked file does that, so only that is checked.
  if ! dirty="$(remote "cd ${remote_path_q} && git status --porcelain --untracked-files=no")"; then
    die "could not check the remote working tree status on ${ssh_host}"
  fi
  if [[ -n "$dirty" ]]; then
    die "the remote working tree has modified tracked files -- 'git pull --ff-only' would fail on this. (.env is gitignored, so it is never the cause.) Resolve or discard the change on the server, then re-run this script."
  fi
}

# ---- 5. confirm -----------------------------------------------------------------------------

confirm_deploy() {
  if [[ "$assume_yes" -eq 1 ]]; then
    return 0
  fi
  local reply
  read -r -p "Deploy the above to ${ssh_host}:${remote_path}? [y/N] " reply
  case "$reply" in
    y|Y|yes|YES|Yes)
      return 0
      ;;
    *)
      echo "Aborted, nothing was touched."
      exit 1
      ;;
  esac
}

# ---- 6. back up the database, before anything else changes ---------------------------------

backup_database() {
  local ts backup_rel
  ts="$(date -u +%Y%m%dT%H%M%SZ)"
  backup_rel="backups/pre-deploy-${ts}.sql"

  echo "Backing up the database to ${backup_rel} on the server..."

  # This is the most important step in the script. Program.cs applies EF Core migrations at
  # worker startup, so pulling new code is not just a code change -- it is an unattended schema
  # change the moment the container comes back up. A dump taken here, before the pull, is the
  # only way back if that migration turns out to be wrong.
  if ! remote "cd ${remote_path_q} && mkdir -p backups && docker compose exec -T postgres pg_dump -U assistant assistant > ${backup_rel}"; then
    die "database backup failed -- aborting before touching anything else. See the pg_dump output above."
  fi

  if ! remote "test -s ${remote_path_q}/${backup_rel}"; then
    die "database backup produced an empty file -- aborting before touching anything else. Remove ${backup_rel} on the server and investigate before retrying."
  fi

  echo "Backup ok: ${backup_rel}"
  backup_path="$backup_rel"

  prune_old_backups
}

prune_old_backups() {
  # ls -t sorts newest first; tail -n +(N+1) keeps only what comes after the Nth newest, and
  # xargs -r skips running rm at all when that list is empty (fewer than N+1 dumps exist yet).
  # Non-fatal on purpose: a pruning hiccup is not a reason to abandon a deploy whose backup
  # already succeeded.
  remote "cd ${remote_path_q}/backups && ls -1t pre-deploy-*.sql 2>/dev/null | tail -n +$((BACKUP_RETAIN_COUNT + 1)) | xargs -r rm --" \
    || echo "warning: could not prune old backups on the server" >&2
}

# ---- 7. pull -----------------------------------------------------------------------------

pull_latest() {
  echo "Pulling the latest main on the server..."
  # --ff-only, never a plain pull: a merge commit created by accident on a server nobody
  # watches interactively is a trap, not a recoverable inconvenience.
  remote "cd ${remote_path_q} && git pull --ff-only" \
    || die "git pull --ff-only failed on the server. The database backup at ${backup_path} is safe; nothing else has changed. Investigate the remote git state before retrying."
}

# ---- 8. rebuild -----------------------------------------------------------------------------

rebuild_containers() {
  echo "Rebuilding and starting containers..."
  remote "cd ${remote_path_q} && docker compose up -d --build" \
    || die "docker compose up -d --build failed on the server. The database backup at ${backup_path} is safe. Check 'docker compose ps' on the server for what state the containers are in."
}

# ---- 9. verify the worker actually came up --------------------------------------------------

# Generates the remote-side polling script. Built here, as one block, so the whole health check
# runs on the server in one SSH round trip -- doing it as repeated local polls would let SSH
# latency eat into the window and would fail the very reachability assumption preflight already
# checked once.
build_health_check_script() {
  cat <<EOF
set -u
cd ${remote_path_q}
worker_id="\$(docker compose ps -q worker)"
if [[ -z "\$worker_id" ]]; then
  echo "no worker container found"
  exit 1
fi
start_restarts="\$(docker inspect -f '{{.RestartCount}}' "\$worker_id")"
elapsed=0
while [[ "\$elapsed" -lt ${HEALTH_CHECK_SECONDS} ]]; do
  status="\$(docker inspect -f '{{.State.Status}}' "\$worker_id" 2>/dev/null || echo gone)"
  if [[ "\$status" != "running" ]]; then
    echo "worker container is not running (status: \$status)"
    exit 1
  fi
  restarts="\$(docker inspect -f '{{.RestartCount}}' "\$worker_id" 2>/dev/null || echo "\$start_restarts")"
  if [[ "\$restarts" -gt "\$start_restarts" ]]; then
    echo "worker container restarted during the health-check window"
    exit 1
  fi
  sleep ${HEALTH_CHECK_POLL_SECONDS}
  elapsed=\$((elapsed + ${HEALTH_CHECK_POLL_SECONDS}))
done
echo "worker is up and stable"
EOF
}

verify_worker_healthy() {
  echo "Waiting up to ${HEALTH_CHECK_SECONDS}s to confirm the worker actually stayed up..."

  local script
  script="$(build_health_check_script)"

  if remote "$script"; then
    echo "Worker is healthy."
    return 0
  fi

  echo "The worker did not come up cleanly. Last logs from the worker container:" >&2
  remote "cd ${remote_path_q} && docker compose logs --tail=40 worker" >&2 || true
  die "deploy failed its post-rebuild health check. The previous version is no longer running. Restore from the pre-deploy backup (${backup_path:-no backup was taken}) on the server if needed."
}

# ---- 10. prune, only after a healthy deploy -------------------------------------------------

prune_docker() {
  echo "Pruning dangling images and build cache..."
  # Non-fatal, same reasoning as prune_old_backups: this runs after the deploy has already
  # pulled, rebuilt, and passed its health check -- it is housekeeping, not part of the deploy
  # itself, so a prune hiccup here must not turn a verified-successful deploy into a reported
  # failure with no summary printed.
  remote "cd ${remote_path_q} && docker image prune -f && docker builder prune -f" \
    || echo "warning: could not prune old images/build cache on the server" >&2
}

# ---- 11. summary -----------------------------------------------------------------------------

print_summary() {
  echo
  echo "Deploy complete."
  echo "Commits deployed:"
  echo "$pending_commits" | sed 's/^/  /'
  echo "Pre-deploy backup: ${backup_path:-none (--no-backup was passed)} (on the server, under ${remote_path})"
}

main() {
  parse_args "$@"

  # Quoted once, up front, for every remote command string built below. Not because
  # remote_path is expected to contain anything unusual -- it is the owner's own config, not
  # untrusted input -- but because building the quoting once here is cheaper than getting it
  # subtly wrong in eleven separate places.
  local remote_path_q
  printf -v remote_path_q '%q' "$remote_path"

  preflight
  check_unpushed_commits

  echo "Checking what would deploy on ${ssh_host}..."
  local pending_commits
  compute_pending_commits
  if [[ -z "$pending_commits" ]]; then
    echo "Already up to date."
    exit 0
  fi
  echo "The following commits would deploy:"
  echo "$pending_commits" | sed 's/^/  /'
  echo

  check_remote_dirty
  confirm_deploy

  if [[ "$skip_backup" -eq 1 ]]; then
    echo "Skipping database backup (--no-backup) -- there is nothing to restore from if this deploy goes wrong."
  else
    backup_database
  fi

  pull_latest
  rebuild_containers
  verify_worker_healthy
  prune_docker
  print_summary
}

main "$@"
