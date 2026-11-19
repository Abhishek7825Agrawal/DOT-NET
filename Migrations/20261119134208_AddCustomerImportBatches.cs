using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerImportBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StaffUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    ValidRowsCount = table.Column<int>(type: "int", nullable: false),
                    InvalidRowsCount = table.Column<int>(type: "int", nullable: false),
                    DuplicateRowsCount = table.Column<int>(type: "int", nullable: false),
                    SuccessImportedCount = table.Column<int>(type: "int", nullable: false),
                    IsCommitted = table.Column<bool>(type: "bit", nullable: false),
                    CommittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerImportBatches_AspNetUsers_StaffUserId",
                        column: x => x.StaffUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerImportRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ValidationMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActivationToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerImportRows_CustomerImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "CustomerImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerImportBatches_BatchReference",
                table: "CustomerImportBatches",
                column: "BatchReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerImportBatches_StaffUserId",
                table: "CustomerImportBatches",
                column: "StaffUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerImportRows_BatchId_RowNumber",
                table: "CustomerImportRows",
                columns: new[] { "BatchId", "RowNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerImportRows");

            migrationBuilder.DropTable(
                name: "CustomerImportBatches");
        }
    }
}
