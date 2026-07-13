using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Octans.Server.Migrations
{
    /// <inheritdoc />
    public partial class ImplementSubscriptionWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                table: "Subscriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Cursor",
                table: "Subscriptions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "Subscriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRunning",
                table: "Subscriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastCompletedAt",
                table: "Subscriptions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "Subscriptions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastStartedAt",
                table: "Subscriptions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxItemsPerRun",
                table: "Subscriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<string>(
                name: "CompletedAt",
                table: "SubscriptionExecutions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Diagnostics",
                table: "SubscriptionExecutions",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportJobId",
                table: "SubscriptionExecutions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ItemsQueued",
                table: "SubscriptionExecutions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ItemsSkipped",
                table: "SubscriptionExecutions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceId",
                table: "ImportJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "ImportJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionExecutionId",
                table: "ImportJobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionId",
                table: "ImportJobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceId",
                table: "ImportItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "ImportItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriptionSourceItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubscriptionId = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstExecutionId = table.Column<int>(type: "INTEGER", nullable: true),
                    LastExecutionId = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RemoteUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    FirstSeenAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<string>(type: "TEXT", nullable: false),
                    QueuedAt = table.Column<string>(type: "TEXT", nullable: true),
                    ImportedAt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionSourceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionSourceItems_SubscriptionExecutions_FirstExecutionId",
                        column: x => x.FirstExecutionId,
                        principalTable: "SubscriptionExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SubscriptionSourceItems_SubscriptionExecutions_LastExecutionId",
                        column: x => x.LastExecutionId,
                        principalTable: "SubscriptionExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SubscriptionSourceItems_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_IsEnabled_NextCheck",
                table: "Subscriptions",
                columns: new[] { "IsEnabled", "NextCheck" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSourceItems_FirstExecutionId",
                table: "SubscriptionSourceItems",
                column: "FirstExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSourceItems_LastExecutionId",
                table: "SubscriptionSourceItems",
                column: "LastExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSourceItems_SubscriptionId_SourceId",
                table: "SubscriptionSourceItems",
                columns: new[] { "SubscriptionId", "SourceId", "RemoteUrl" },
                unique: true);

            migrationBuilder.Sql("UPDATE Subscriptions SET IsEnabled = 1 WHERE IsEnabled = 0");
            migrationBuilder.Sql("UPDATE Subscriptions SET MaxItemsPerRun = 100 WHERE MaxItemsPerRun = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionSourceItems");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_IsEnabled_NextCheck",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "Cursor",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "IsRunning",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "LastCompletedAt",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "LastStartedAt",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "MaxItemsPerRun",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "SubscriptionExecutions");

            migrationBuilder.DropColumn(
                name: "Diagnostics",
                table: "SubscriptionExecutions");

            migrationBuilder.DropColumn(
                name: "ImportJobId",
                table: "SubscriptionExecutions");

            migrationBuilder.DropColumn(
                name: "ItemsQueued",
                table: "SubscriptionExecutions");

            migrationBuilder.DropColumn(
                name: "ItemsSkipped",
                table: "SubscriptionExecutions");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "SubscriptionExecutionId",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "SubscriptionId",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "ImportItems");
        }
    }
}
