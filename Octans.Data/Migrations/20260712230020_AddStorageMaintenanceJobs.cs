using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Octans.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageMaintenanceJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StorageMaintenanceJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Trigger = table.Column<int>(type: "INTEGER", nullable: false),
                    RepairActions = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceScanJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TotalItems = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessedItems = table.Column<int>(type: "INTEGER", nullable: false),
                    FindingsCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RepairedItems = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedItems = table.Column<int>(type: "INTEGER", nullable: false),
                    ScannedBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrentItem = table.Column<string>(type: "TEXT", nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageMaintenanceJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageMaintenanceJobs_StorageMaintenanceJobs_SourceScanJobId",
                        column: x => x.SourceScanJobId,
                        principalTable: "StorageMaintenanceJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorageMaintenanceFindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScanJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Path = table.Column<string>(type: "TEXT", nullable: true),
                    ExpectedPath = table.Column<string>(type: "TEXT", nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Resolution = table.Column<int>(type: "INTEGER", nullable: false),
                    RepairJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResolvedAt = table.Column<string>(type: "TEXT", nullable: true),
                    ResolutionMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageMaintenanceFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageMaintenanceFindings_StorageMaintenanceJobs_RepairJobId",
                        column: x => x.RepairJobId,
                        principalTable: "StorageMaintenanceJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorageMaintenanceFindings_StorageMaintenanceJobs_ScanJobId",
                        column: x => x.ScanJobId,
                        principalTable: "StorageMaintenanceJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StorageMaintenanceFindings_RepairJobId",
                table: "StorageMaintenanceFindings",
                column: "RepairJobId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageMaintenanceFindings_ScanJobId_Resolution_Type",
                table: "StorageMaintenanceFindings",
                columns: ["ScanJobId", "Resolution", "Type"]);

            migrationBuilder.CreateIndex(
                name: "IX_StorageMaintenanceJobs_SourceScanJobId",
                table: "StorageMaintenanceJobs",
                column: "SourceScanJobId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageMaintenanceJobs_Status_CreatedAt",
                table: "StorageMaintenanceJobs",
                columns: ["Status", "CreatedAt"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorageMaintenanceFindings");

            migrationBuilder.DropTable(
                name: "StorageMaintenanceJobs");
        }
    }
}
