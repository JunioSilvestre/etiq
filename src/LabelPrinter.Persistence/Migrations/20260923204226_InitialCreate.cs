using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelPrinter.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveredPrinters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WindowsName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DriverName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PortName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsOnline = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PrinterStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveredPrinters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrinterProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    WindowsPrinterName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DriverName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PortName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Dpi = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultLabelWidthMm = table.Column<double>(type: "REAL", nullable: false),
                    DefaultLabelHeightMm = table.Column<double>(type: "REAL", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxRetries = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryDelaySeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    PrintTimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultCopies = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrinterRoutingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrinterProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Marketplace = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    LabelSize = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterRoutingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrinterRoutingRules_PrinterProfiles_PrinterProfileId",
                        column: x => x.PrinterProfileId,
                        principalTable: "PrinterProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrintJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalFilePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OriginalHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DocumentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LabelHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PdfPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ArchivePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ExtractedPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Marketplace = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    LabelSize = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    MarketplaceRaw = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PrinterProfileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OrderReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    TrackingNumber = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PackageNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    PackageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    IsReprint = table.Column<bool>(type: "INTEGER", nullable: false),
                    OriginalJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DetectedEncoding = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintJobs_PrinterProfiles_PrinterProfileId",
                        column: x => x.PrinterProfileId,
                        principalTable: "PrinterProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Metadata = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEvents_PrintJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "PrintJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PrintAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Result = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    SpoolerJobId = table.Column<int>(type: "INTEGER", nullable: true),
                    PrinterName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintAttempts_PrintJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "PrintJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CreatedAt",
                table: "AuditEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_EventType",
                table: "AuditEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_JobId",
                table: "AuditEvents",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredPrinters_WindowsName",
                table: "DiscoveredPrinters",
                column: "WindowsName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintAttempts_JobId",
                table: "PrintAttempts",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterProfiles_Enabled",
                table: "PrinterProfiles",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterProfiles_WindowsPrinterName",
                table: "PrinterProfiles",
                column: "WindowsPrinterName");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterRoutingRules_PrinterProfileId",
                table: "PrinterRoutingRules",
                column: "PrinterProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_CreatedAt",
                table: "PrintJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_Marketplace",
                table: "PrintJobs",
                column: "Marketplace");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_OriginalHash",
                table: "PrintJobs",
                column: "OriginalHash");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_PrinterProfileId",
                table: "PrintJobs",
                column: "PrinterProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_Status",
                table: "PrintJobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "DiscoveredPrinters");

            migrationBuilder.DropTable(
                name: "PrintAttempts");

            migrationBuilder.DropTable(
                name: "PrinterRoutingRules");

            migrationBuilder.DropTable(
                name: "PrintJobs");

            migrationBuilder.DropTable(
                name: "PrinterProfiles");
        }
    }
}
