using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MessedUpSearchA.Migrations
{
    /// <inheritdoc />
    public partial class AddSimilarityTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "BeatSimilarities",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "BeatSimilarities",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "BeatSimilarities",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Plays",
                table: "BeatSimilarities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceId",
                table: "BeatSimilarities",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "BeatSimilarities",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TopTrackId",
                table: "BeatSimilarities",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TopTrackTitle",
                table: "BeatSimilarities",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TopTrackUrl",
                table: "BeatSimilarities",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "BeatSimilarities");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "BeatSimilarities");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "BeatSimilarities");

            migrationBuilder.DropColumn(
                name: "Plays",
                table: "BeatSimilarities");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "BeatSimilarities");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "BeatSimilarities");

            migrationBuilder.DropColumn(
                name: "TopTrackId",
                table: "BeatSimilarities");

            migrationBuilder.DropColumn(
                name: "TopTrackTitle",
                table: "BeatSimilarities");

            migrationBuilder.DropColumn(
                name: "TopTrackUrl",
                table: "BeatSimilarities");
        }
    }
}
