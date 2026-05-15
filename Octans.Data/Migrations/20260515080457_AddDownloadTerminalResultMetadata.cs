using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Octans.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadTerminalResultMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailureCategory",
                table: "DownloadStatuses",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HttpStatusCode",
                table: "DownloadStatuses",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseContentType",
                table: "DownloadStatuses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseETag",
                table: "DownloadStatuses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseLastModified",
                table: "DownloadStatuses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TerminalOutcome",
                table: "DownloadStatuses",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationMessage",
                table: "DownloadStatuses",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailureCategory",
                table: "DownloadStatuses");

            migrationBuilder.DropColumn(
                name: "HttpStatusCode",
                table: "DownloadStatuses");

            migrationBuilder.DropColumn(
                name: "ResponseContentType",
                table: "DownloadStatuses");

            migrationBuilder.DropColumn(
                name: "ResponseETag",
                table: "DownloadStatuses");

            migrationBuilder.DropColumn(
                name: "ResponseLastModified",
                table: "DownloadStatuses");

            migrationBuilder.DropColumn(
                name: "TerminalOutcome",
                table: "DownloadStatuses");

            migrationBuilder.DropColumn(
                name: "ValidationMessage",
                table: "DownloadStatuses");
        }
    }
}
