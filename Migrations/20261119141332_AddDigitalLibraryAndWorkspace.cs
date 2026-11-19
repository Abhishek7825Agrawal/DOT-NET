using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddDigitalLibraryAndWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DigitalBooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    StorageFileName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: false),
                    DocumentVersion = table.Column<int>(type: "int", nullable: false),
                    IsDigitalAvailable = table.Column<bool>(type: "bit", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ChecksumSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigitalBooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DigitalBooks_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentDeltaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SanitizedContentHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlainTextExcerpt = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserNotes_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookBookmarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DigitalBookId = table.Column<int>(type: "int", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookBookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookBookmarks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookBookmarks_DigitalBooks_DigitalBookId",
                        column: x => x.DigitalBookId,
                        principalTable: "DigitalBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingProgresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DigitalBookId = table.Column<int>(type: "int", nullable: false),
                    LastPageRead = table.Column<int>(type: "int", nullable: false),
                    TotalPages = table.Column<int>(type: "int", nullable: false),
                    PercentComplete = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    LastReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingProgresses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReadingProgresses_DigitalBooks_DigitalBookId",
                        column: x => x.DigitalBookId,
                        principalTable: "DigitalBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookAnnotations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DigitalBookId = table.Column<int>(type: "int", nullable: false),
                    DocumentVersion = table.Column<int>(type: "int", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    SelectedText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    HighlightColor = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TextAnchorJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PositionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LinkedNoteId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookAnnotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookAnnotations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookAnnotations_DigitalBooks_DigitalBookId",
                        column: x => x.DigitalBookId,
                        principalTable: "DigitalBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookAnnotations_UserNotes_LinkedNoteId",
                        column: x => x.LinkedNoteId,
                        principalTable: "UserNotes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookAnnotations_DigitalBookId_UserId_PageNumber",
                table: "BookAnnotations",
                columns: new[] { "DigitalBookId", "UserId", "PageNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_BookAnnotations_LinkedNoteId",
                table: "BookAnnotations",
                column: "LinkedNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_BookAnnotations_UserId",
                table: "BookAnnotations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BookBookmarks_DigitalBookId",
                table: "BookBookmarks",
                column: "DigitalBookId");

            migrationBuilder.CreateIndex(
                name: "IX_BookBookmarks_UserId_DigitalBookId_PageNumber",
                table: "BookBookmarks",
                columns: new[] { "UserId", "DigitalBookId", "PageNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_DigitalBooks_BookId",
                table: "DigitalBooks",
                column: "BookId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingProgresses_DigitalBookId",
                table: "ReadingProgresses",
                column: "DigitalBookId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingProgresses_UserId_DigitalBookId",
                table: "ReadingProgresses",
                columns: new[] { "UserId", "DigitalBookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotes_BookId",
                table: "UserNotes",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotes_UserId_BookId",
                table: "UserNotes",
                columns: new[] { "UserId", "BookId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookAnnotations");

            migrationBuilder.DropTable(
                name: "BookBookmarks");

            migrationBuilder.DropTable(
                name: "ReadingProgresses");

            migrationBuilder.DropTable(
                name: "UserNotes");

            migrationBuilder.DropTable(
                name: "DigitalBooks");
        }
    }
}
