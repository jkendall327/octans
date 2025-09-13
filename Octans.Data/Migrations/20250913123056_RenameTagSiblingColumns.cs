using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Octans.Server.Migrations
{
    /// <inheritdoc />
    public partial class RenameTagSiblingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TagSiblings_Tags_BetterId",
                table: "TagSiblings");

            migrationBuilder.DropForeignKey(
                name: "FK_TagSiblings_Tags_WorseId",
                table: "TagSiblings");

            migrationBuilder.RenameColumn(
                name: "WorseId",
                table: "TagSiblings",
                newName: "NonIdealId");

            migrationBuilder.RenameColumn(
                name: "BetterId",
                table: "TagSiblings",
                newName: "IdealId");

            migrationBuilder.RenameIndex(
                name: "IX_TagSiblings_WorseId",
                table: "TagSiblings",
                newName: "IX_TagSiblings_NonIdealId");

            migrationBuilder.RenameIndex(
                name: "IX_TagSiblings_BetterId",
                table: "TagSiblings",
                newName: "IX_TagSiblings_IdealId");

            migrationBuilder.AddForeignKey(
                name: "FK_TagSiblings_Tags_IdealId",
                table: "TagSiblings",
                column: "IdealId",
                principalTable: "Tags",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TagSiblings_Tags_NonIdealId",
                table: "TagSiblings",
                column: "NonIdealId",
                principalTable: "Tags",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TagSiblings_Tags_IdealId",
                table: "TagSiblings");

            migrationBuilder.DropForeignKey(
                name: "FK_TagSiblings_Tags_NonIdealId",
                table: "TagSiblings");

            migrationBuilder.RenameColumn(
                name: "NonIdealId",
                table: "TagSiblings",
                newName: "WorseId");

            migrationBuilder.RenameColumn(
                name: "IdealId",
                table: "TagSiblings",
                newName: "BetterId");

            migrationBuilder.RenameIndex(
                name: "IX_TagSiblings_NonIdealId",
                table: "TagSiblings",
                newName: "IX_TagSiblings_WorseId");

            migrationBuilder.RenameIndex(
                name: "IX_TagSiblings_IdealId",
                table: "TagSiblings",
                newName: "IX_TagSiblings_BetterId");

            migrationBuilder.AddForeignKey(
                name: "FK_TagSiblings_Tags_BetterId",
                table: "TagSiblings",
                column: "BetterId",
                principalTable: "Tags",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TagSiblings_Tags_WorseId",
                table: "TagSiblings",
                column: "WorseId",
                principalTable: "Tags",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
