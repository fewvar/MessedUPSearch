using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MessedUpSearchA.Migrations
{
    /// <inheritdoc />
    public partial class AddReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IncomingReplies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ArtistId = table.Column<int>(type: "INTEGER", nullable: true),
                    OutgoingMailId = table.Column<int>(type: "INTEGER", nullable: true),
                    MessageId = table.Column<string>(type: "TEXT", nullable: false),
                    FromAddress = table.Column<string>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    Snippet = table.Column<string>(type: "TEXT", nullable: false),
                    ReceivedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingReplies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncomingReplies_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_IncomingReplies_OutgoingMails_OutgoingMailId",
                        column: x => x.OutgoingMailId,
                        principalTable: "OutgoingMails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncomingReplies_ArtistId",
                table: "IncomingReplies",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_IncomingReplies_MessageId",
                table: "IncomingReplies",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_IncomingReplies_OutgoingMailId",
                table: "IncomingReplies",
                column: "OutgoingMailId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncomingReplies");
        }
    }
}
