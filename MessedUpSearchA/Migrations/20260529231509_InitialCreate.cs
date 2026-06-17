using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MessedUpSearchA.Migrations
{

    public partial class InitialCreate : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Artists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nickname = table.Column<string>(type: "TEXT", nullable: false),
                    ScLink = table.Column<string>(type: "TEXT", nullable: false),
                    AvatarPath = table.Column<string>(type: "TEXT", nullable: false),
                    AvatarColor = table.Column<string>(type: "TEXT", nullable: false),
                    IgLink = table.Column<string>(type: "TEXT", nullable: false),
                    SpotifyLink = table.Column<string>(type: "TEXT", nullable: false),
                    LinktreeLink = table.Column<string>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    TotalPlays = table.Column<int>(type: "INTEGER", nullable: false),
                    LastTrackDate = table.Column<string>(type: "TEXT", nullable: false),
                    AiGenreTags = table.Column<string>(type: "TEXT", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRedFlagged = table.Column<bool>(type: "INTEGER", nullable: false),
                    CrmStatus = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastParsed = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Beats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BeatName = table.Column<string>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    Bpm = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Added = table.Column<string>(type: "TEXT", nullable: false),
                    AiTags = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    StatusColor = table.Column<string>(type: "TEXT", nullable: false),
                    LicenseType = table.Column<string>(type: "TEXT", nullable: false),
                    IsSold = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Beats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SentBeatsLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ArtistId = table.Column<int>(type: "INTEGER", nullable: false),
                    BeatId = table.Column<int>(type: "INTEGER", nullable: false),
                    SentAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentBeatsLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SentBeatsLog_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SentBeatsLog_Beats_BeatId",
                        column: x => x.BeatId,
                        principalTable: "Beats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Artists_ScLink",
                table: "Artists",
                column: "ScLink",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SentBeatsLog_ArtistId",
                table: "SentBeatsLog",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_SentBeatsLog_BeatId",
                table: "SentBeatsLog",
                column: "BeatId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SentBeatsLog");

            migrationBuilder.DropTable(
                name: "Artists");

            migrationBuilder.DropTable(
                name: "Beats");
        }
    }
}
