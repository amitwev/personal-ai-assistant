using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "message_id",
                table: "reminder_tasks",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "message_id",
                table: "reminder_tasks");
        }
    }
}
