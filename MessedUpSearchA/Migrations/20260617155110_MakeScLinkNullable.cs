using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MessedUpSearchA.Migrations
{
    /// <inheritdoc />
    public partial class MakeScLinkNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Artists_ScLink",
                table: "Artists");

            migrationBuilder.AlterColumn<string>(
                name: "ScLink",
                table: "Artists",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.CreateIndex(
                name: "IX_Artists_ScLink",
                table: "Artists",
                column: "ScLink",
                unique: true,
                filter: "\"ScLink\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Artists_ScLink",
                table: "Artists");

            migrationBuilder.AlterColumn<string>(
                name: "ScLink",
                table: "Artists",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Artists_ScLink",
                table: "Artists",
                column: "ScLink",
                unique: true);
        }
    }
}
