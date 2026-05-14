using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Octans.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadJobProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "QueuedDownloads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceId",
                table: "QueuedDownloads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "QueuedDownloads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "DownloadStatuses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceId",
                table: "DownloadStatuses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "DownloadStatuses",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "QueuedDownloads");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "QueuedDownloads");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "QueuedDownloads");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "DownloadStatuses");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "DownloadStatuses");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "DownloadStatuses");
        }
    }
}
