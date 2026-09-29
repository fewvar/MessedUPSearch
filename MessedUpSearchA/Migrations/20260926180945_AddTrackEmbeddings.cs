using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MessedUpSearchA.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ArtistId",
                table: "BeatSimilarities",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmbedAttempts",
                table: "ArtistTracks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EmbedError",
                table: "ArtistTracks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceTrackId",
                table: "ArtistTracks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DeezerCheckedAt",
                table: "Artists",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "TrackEmbeddings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ArtistTrackId = table.Column<int>(type: "INTEGER", nullable: false),
                    Vector = table.Column<byte[]>(type: "BLOB", nullable: false),
                    AudioKind = table.Column<string>(type: "TEXT", nullable: false),
                    Seconds = table.Column<double>(type: "REAL", nullable: false),
                    ModelVersion = table.Column<string>(type: "TEXT", nullable: false),
                    ComputedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackEmbeddings_ArtistTracks_ArtistTrackId",
                        column: x => x.ArtistTrackId,
                        principalTable: "ArtistTracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BeatSimilarities_ArtistId",
                table: "BeatSimilarities",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackEmbeddings_ArtistTrackId_ModelVersion",
                table: "TrackEmbeddings",
                columns: new[] { "ArtistTrackId", "ModelVersion" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BeatSimilarities_Artists_ArtistId",
                table: "BeatSimilarities",
                column: "ArtistId",
                principalTable: "Artists",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BeatSimilarities_Artists_ArtistId",
                table: "BeatSimilarities");

            migrationBuilder.DropTable(
                name: "TrackEmbeddings");

            migrationBuilder.DropIndex(
                name: "IX_BeatSimilarities_ArtistId",
                table: "BeatSimilarities");

            migrationBuilder.DropColumn(
                name: "ArtistId",
                table: "BeatSimilarities");

            migrationBuilder.DropColumn(
                name: "EmbedAttempts",
                table: "ArtistTracks");

            migrationBuilder.DropColumn(
                name: "EmbedError",
                table: "ArtistTracks");

            migrationBuilder.DropColumn(
                name: "SourceTrackId",
                table: "ArtistTracks");

            migrationBuilder.DropColumn(
                name: "DeezerCheckedAt",
                table: "Artists");
        }
    }
}
