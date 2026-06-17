using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MessedUpSearchA.Migrations
{

    public partial class AddReminderFieldsToSentBeatsLog : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedAt",
                table: "SentBeatsLog",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsSent",
                table: "SentBeatsLog",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "SentBeatsLog");

            migrationBuilder.DropColumn(
                name: "IsSent",
                table: "SentBeatsLog");
        }
    }
}
