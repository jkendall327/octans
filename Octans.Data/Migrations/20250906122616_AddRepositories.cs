using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Octans.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositories : Migration
    {
        private static readonly string[] columns = new[] { "Id", "Name" };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Repositories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repositories", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Repositories",
                columns: columns,
#pragma warning disable CA1814
                values: new object[,]
                {
                    { 1, "Inbox" },
                    { 2, "Archive" },
                    { 3, "Trash" }
                });
#pragma warning restore CA1814

            migrationBuilder.AddColumn<int>(
                name: "RepositoryId",
                table: "Hashes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Hashes_RepositoryId",
                table: "Hashes",
                column: "RepositoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Hashes_Repositories_RepositoryId",
                table: "Hashes",
                column: "RepositoryId",
                principalTable: "Repositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Hashes_Repositories_RepositoryId",
                table: "Hashes");

            migrationBuilder.DropTable(
                name: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Hashes_RepositoryId",
                table: "Hashes");

            migrationBuilder.DropColumn(
                name: "RepositoryId",
                table: "Hashes");
        }
    }
}
