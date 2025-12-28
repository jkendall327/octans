using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Octans.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRatingSystems : Migration
    {
        private static readonly string[] columns = new[] { "Id", "MaxValue", "Name", "Type" };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RatingSystems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxValue = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RatingSystems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HashRatings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HashId = table.Column<int>(type: "INTEGER", nullable: false),
                    RatingSystemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HashRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HashRatings_Hashes_HashId",
                        column: x => x.HashId,
                        principalTable: "Hashes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HashRatings_RatingSystems_RatingSystemId",
                        column: x => x.RatingSystemId,
                        principalTable: "RatingSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "RatingSystems",
                columns: columns,
                values: new object[,]
                {
                    { 1, 1, "Favourites", 0 },
                    { 2, 5, "Quality", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_HashRatings_HashId",
                table: "HashRatings",
                column: "HashId");

            migrationBuilder.CreateIndex(
                name: "IX_HashRatings_RatingSystemId",
                table: "HashRatings",
                column: "RatingSystemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HashRatings");

            migrationBuilder.DropTable(
                name: "RatingSystems");
        }
    }
}
