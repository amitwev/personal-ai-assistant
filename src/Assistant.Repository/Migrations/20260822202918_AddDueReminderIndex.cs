using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddDueReminderIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_tasks_due_pending",
                table: "reminder_tasks",
                column: "due_at",
                filter: "status = 1 AND reminder_sent_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_tasks_due_pending",
                table: "reminder_tasks");
        }
    }
}
