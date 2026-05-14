using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Octans.Server.Migrations
{
    /// <inheritdoc />
    public partial class DurableImportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowReimportDeleted",
                table: "ImportJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoArchive",
                table: "ImportJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "ImportJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentItem",
                table: "ImportJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeleteAfterImport",
                table: "ImportJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "FailedItems",
                table: "ImportJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "ImportJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Phase",
                table: "ImportJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProcessedItems",
                table: "ImportJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SerializedFilterData",
                table: "ImportJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAt",
                table: "ImportJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalItems",
                table: "ImportJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "ImportJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "ImportItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "ImportItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "ImportItems",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "ImportType",
                table: "ImportItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SerializedTags",
                table: "ImportItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAt",
                table: "ImportItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "ImportItems",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowReimportDeleted",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "AutoArchive",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "CurrentItem",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "DeleteAfterImport",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "FailedItems",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "Phase",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "ProcessedItems",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "SerializedFilterData",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "TotalItems",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "ImportType",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "SerializedTags",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ImportItems");
        }
    }
}
