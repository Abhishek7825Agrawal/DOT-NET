using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchPaperMarketplace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketplaceSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlatformCommissionRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    RequireAdminApproval = table.Column<bool>(type: "bit", nullable: false),
                    MinimumListingPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaximumListingPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResearchPapers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Abstract = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Authors = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Affiliation = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Keywords = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Doi = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PublicationYear = table.Column<int>(type: "int", nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EditorialFeedback = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StorageFileName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CurrentVersion = table.Column<int>(type: "int", nullable: false),
                    CopyrightAccepted = table.Column<bool>(type: "bit", nullable: false),
                    CopyrightAcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmitterUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DownloadCount = table.Column<int>(type: "int", nullable: false),
                    PurchaseCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchPapers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchPapers_AspNetUsers_SubmitterUserId",
                        column: x => x.SubmitterUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaperCopyrightReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaperId = table.Column<int>(type: "int", nullable: false),
                    ReporterUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReporterName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ReporterEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InfringedWorkTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    InfringementDescription = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GoodFaithDeclaration = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AdminRemarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperCopyrightReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaperCopyrightReports_AspNetUsers_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaperCopyrightReports_ResearchPapers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "ResearchPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaperOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PaperId = table.Column<int>(type: "int", nullable: false),
                    BuyerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PlatformCommissionRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    PlatformCommissionAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AuthorEarningsAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentStatus = table.Column<int>(type: "int", nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PaymentReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OrderDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaperOrders_AspNetUsers_BuyerUserId",
                        column: x => x.BuyerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaperOrders_ResearchPapers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "ResearchPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaperVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaperId = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    StorageFileName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ChangeSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaperVersions_ResearchPapers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "ResearchPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperCopyrightReports_PaperId_Status",
                table: "PaperCopyrightReports",
                columns: new[] { "PaperId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperCopyrightReports_ReporterUserId",
                table: "PaperCopyrightReports",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperOrders_BuyerUserId_PaperId_PaymentStatus",
                table: "PaperOrders",
                columns: new[] { "BuyerUserId", "PaperId", "PaymentStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperOrders_OrderNumber",
                table: "PaperOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaperOrders_PaperId",
                table: "PaperOrders",
                column: "PaperId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperVersions_PaperId_VersionNumber",
                table: "PaperVersions",
                columns: new[] { "PaperId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResearchPapers_Status_Category",
                table: "ResearchPapers",
                columns: new[] { "Status", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchPapers_SubmitterUserId",
                table: "ResearchPapers",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchPapers_Title",
                table: "ResearchPapers",
                column: "Title");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketplaceSettings");

            migrationBuilder.DropTable(
                name: "PaperCopyrightReports");

            migrationBuilder.DropTable(
                name: "PaperOrders");

            migrationBuilder.DropTable(
                name: "PaperVersions");

            migrationBuilder.DropTable(
                name: "ResearchPapers");
        }
    }
}
