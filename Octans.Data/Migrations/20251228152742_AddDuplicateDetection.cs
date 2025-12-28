using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Octans.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDuplicateDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<ulong>(
                name: "PerceptualHash",
                table: "Hashes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DuplicateCandidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HashId1 = table.Column<int>(type: "INTEGER", nullable: false),
                    HashId2 = table.Column<int>(type: "INTEGER", nullable: false),
                    Distance = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DuplicateCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DuplicateCandidates_Hashes_HashId1",
                        column: x => x.HashId1,
                        principalTable: "Hashes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DuplicateCandidates_Hashes_HashId2",
                        column: x => x.HashId2,
                        principalTable: "Hashes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DuplicateDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HashId1 = table.Column<int>(type: "INTEGER", nullable: false),
                    HashId2 = table.Column<int>(type: "INTEGER", nullable: false),
                    Resolution = table.Column<int>(type: "INTEGER", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DuplicateDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DuplicateDecisions_Hashes_HashId1",
                        column: x => x.HashId1,
                        principalTable: "Hashes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DuplicateDecisions_Hashes_HashId2",
                        column: x => x.HashId2,
                        principalTable: "Hashes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateCandidates_HashId1",
                table: "DuplicateCandidates",
                column: "HashId1");

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateCandidates_HashId2",
                table: "DuplicateCandidates",
                column: "HashId2");

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateDecisions_HashId1",
                table: "DuplicateDecisions",
                column: "HashId1");

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateDecisions_HashId2",
                table: "DuplicateDecisions",
                column: "HashId2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DuplicateCandidates");

            migrationBuilder.DropTable(
                name: "DuplicateDecisions");

            migrationBuilder.DropColumn(
                name: "PerceptualHash",
                table: "Hashes");
        }
    }
}
