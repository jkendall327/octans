using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Octans.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadRequestFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "QueuedDownloads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "DownloadStatuses",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "QueuedDownloads");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "DownloadStatuses");
        }
    }
}
