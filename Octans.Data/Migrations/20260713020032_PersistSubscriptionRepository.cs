using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Octans.Server.Migrations
{
    /// <inheritdoc />
    public partial class PersistSubscriptionRepository : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RepositoryId",
                table: "ImportJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "DownloadId",
                table: "ImportItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("UPDATE ImportJobs SET RepositoryId = CASE WHEN AutoArchive = 1 THEN 2 ELSE 1 END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RepositoryId",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "DownloadId",
                table: "ImportItems");
        }
    }
}
