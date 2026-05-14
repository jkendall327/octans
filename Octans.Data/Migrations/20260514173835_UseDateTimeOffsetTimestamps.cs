using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Octans.Server.Migrations
{
    /// <inheritdoc />
    public partial class UseDateTimeOffsetTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            NormalizeToUtcOffset(migrationBuilder, "DownloadStatuses", "CompletedAt");
            NormalizeToUtcOffset(migrationBuilder, "DownloadStatuses", "CreatedAt");
            NormalizeToUtcOffset(migrationBuilder, "DownloadStatuses", "LastUpdated");
            NormalizeToUtcOffset(migrationBuilder, "DownloadStatuses", "StartedAt");
            NormalizeToUtcOffset(migrationBuilder, "DuplicateCandidates", "CreatedAt");
            NormalizeToUtcOffset(migrationBuilder, "DuplicateDecisions", "DecidedAt");
            NormalizeToUtcOffset(migrationBuilder, "Hashes", "DeletedAt");
            NormalizeToUtcOffset(migrationBuilder, "ImportItems", "CompletedAt");
            NormalizeToUtcOffset(migrationBuilder, "ImportItems", "CreatedAt");
            NormalizeToUtcOffset(migrationBuilder, "ImportItems", "StartedAt");
            NormalizeToUtcOffset(migrationBuilder, "ImportItems", "UpdatedAt");
            NormalizeToUtcOffset(migrationBuilder, "ImportJobs", "CompletedAt");
            NormalizeToUtcOffset(migrationBuilder, "ImportJobs", "CreatedAt");
            NormalizeToUtcOffset(migrationBuilder, "ImportJobs", "StartedAt");
            NormalizeToUtcOffset(migrationBuilder, "ImportJobs", "UpdatedAt");
            NormalizeToUtcOffset(migrationBuilder, "Notes", "CreatedAt");
            NormalizeToUtcOffset(migrationBuilder, "Notes", "LastModifiedAt");
            NormalizeToUtcOffset(migrationBuilder, "QueuedDownloads", "QueuedAt");
            NormalizeToUtcOffset(migrationBuilder, "SubscriptionExecutions", "ExecutedAt");
            NormalizeToUtcOffset(migrationBuilder, "Subscriptions", "NextCheck");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            StripUtcOffset(migrationBuilder, "DownloadStatuses", "CompletedAt");
            StripUtcOffset(migrationBuilder, "DownloadStatuses", "CreatedAt");
            StripUtcOffset(migrationBuilder, "DownloadStatuses", "LastUpdated");
            StripUtcOffset(migrationBuilder, "DownloadStatuses", "StartedAt");
            StripUtcOffset(migrationBuilder, "DuplicateCandidates", "CreatedAt");
            StripUtcOffset(migrationBuilder, "DuplicateDecisions", "DecidedAt");
            StripUtcOffset(migrationBuilder, "Hashes", "DeletedAt");
            StripUtcOffset(migrationBuilder, "ImportItems", "CompletedAt");
            StripUtcOffset(migrationBuilder, "ImportItems", "CreatedAt");
            StripUtcOffset(migrationBuilder, "ImportItems", "StartedAt");
            StripUtcOffset(migrationBuilder, "ImportItems", "UpdatedAt");
            StripUtcOffset(migrationBuilder, "ImportJobs", "CompletedAt");
            StripUtcOffset(migrationBuilder, "ImportJobs", "CreatedAt");
            StripUtcOffset(migrationBuilder, "ImportJobs", "StartedAt");
            StripUtcOffset(migrationBuilder, "ImportJobs", "UpdatedAt");
            StripUtcOffset(migrationBuilder, "Notes", "CreatedAt");
            StripUtcOffset(migrationBuilder, "Notes", "LastModifiedAt");
            StripUtcOffset(migrationBuilder, "QueuedDownloads", "QueuedAt");
            StripUtcOffset(migrationBuilder, "SubscriptionExecutions", "ExecutedAt");
            StripUtcOffset(migrationBuilder, "Subscriptions", "NextCheck");
        }

        private static void NormalizeToUtcOffset(MigrationBuilder migrationBuilder, string table, string column)
        {
            migrationBuilder.Sql($"""
                UPDATE "{table}"
                SET "{column}" = strftime('%Y-%m-%dT%H:%M:%f', "{column}") || '+00:00'
                WHERE "{column}" IS NOT NULL;
                """);
        }

        private static void StripUtcOffset(MigrationBuilder migrationBuilder, string table, string column)
        {
            migrationBuilder.Sql($"""
                UPDATE "{table}"
                SET "{column}" = strftime('%Y-%m-%dT%H:%M:%f', "{column}")
                WHERE "{column}" IS NOT NULL;
                """);
        }
    }
}
