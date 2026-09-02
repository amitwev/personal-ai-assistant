using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddCompletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at",
                table: "reminder_tasks",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_reminder_tasks_completed_consistency",
                table: "reminder_tasks",
                sql: "(status = 2) = (completed_at IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reminder_tasks_completed_consistency",
                table: "reminder_tasks");

            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "reminder_tasks");
        }
    }
}
