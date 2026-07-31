using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MessedUpSearchA.Migrations
{
    /// <inheritdoc />
    public partial class AddArtistTracksAndSourceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourcePlatform",
                table: "Artists",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "Artists",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArtistTracks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ArtistId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: true),
                    SourcePlatform = table.Column<string>(type: "TEXT", nullable: false),
                    PlayCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ReleasedAt = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    IsDownloadable = table.Column<bool>(type: "INTEGER", nullable: false),
                    LocalFilePath = table.Column<string>(type: "TEXT", nullable: false),
                    FetchedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArtistTracks_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Artists_SourceUrl",
                table: "Artists",
                column: "SourceUrl",
                unique: true,
                filter: "\"SourceUrl\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ArtistTracks_ArtistId",
                table: "ArtistTracks",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtistTracks_Url",
                table: "ArtistTracks",
                column: "Url",
                unique: true,
                filter: "\"Url\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtistTracks");

            migrationBuilder.DropIndex(
                name: "IX_Artists_SourceUrl",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "SourcePlatform",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "Artists");
        }
    }
}
