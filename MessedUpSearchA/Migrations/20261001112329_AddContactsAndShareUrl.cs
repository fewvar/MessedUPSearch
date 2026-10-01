using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MessedUpSearchA.Migrations
{
    /// <inheritdoc />
    public partial class AddContactsAndShareUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShareUrl",
                table: "Beats",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Bio",
                table: "Artists",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Artists",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OtherContact",
                table: "Artists",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Telegram",
                table: "Artists",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShareUrl",
                table: "Beats");

            migrationBuilder.DropColumn(
                name: "Bio",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "OtherContact",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "Telegram",
                table: "Artists");
        }
    }
}
