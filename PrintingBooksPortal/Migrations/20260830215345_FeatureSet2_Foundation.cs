using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintingBooksPortal.Migrations
{
    /// <inheritdoc />
    public partial class FeatureSet2_Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ActorRole = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StudentAnnotationRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    StudentId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    AnnotationData = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastSavedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentAnnotationRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentAnnotationRecords_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StudentAnnotationRecords_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentAnnotationRecords_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentBookmarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    StudentId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentBookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentBookmarks_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StudentBookmarks_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentBookmarks_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    StudentId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RelatedUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentNotifications_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StudentReadingProgresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    StudentId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    LastPageRead = table.Column<int>(type: "int", nullable: false),
                    TotalPages = table.Column<int>(type: "int", nullable: false),
                    LastReadAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentReadingProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentReadingProgresses_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StudentReadingProgresses_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentReadingProgresses_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeacherAnnouncements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherAnnouncements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherAnnouncements_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OccurredAt",
                table: "AuditLogs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAnnotationRecords_BookId",
                table: "StudentAnnotationRecords",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAnnotationRecords_StudentId",
                table: "StudentAnnotationRecords",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAnnotationRecords_StudentUserId_BookId_PageNumber",
                table: "StudentAnnotationRecords",
                columns: new[] { "StudentUserId", "BookId", "PageNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentAnnotationRecords_TenantId",
                table: "StudentAnnotationRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBookmarks_BookId",
                table: "StudentBookmarks",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBookmarks_StudentId",
                table: "StudentBookmarks",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBookmarks_StudentUserId_BookId",
                table: "StudentBookmarks",
                columns: new[] { "StudentUserId", "BookId" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentBookmarks_TenantId",
                table: "StudentBookmarks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentNotifications_StudentId",
                table: "StudentNotifications",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentNotifications_StudentUserId_IsRead",
                table: "StudentNotifications",
                columns: new[] { "StudentUserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentReadingProgresses_BookId",
                table: "StudentReadingProgresses",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentReadingProgresses_StudentId",
                table: "StudentReadingProgresses",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentReadingProgresses_StudentUserId_BookId",
                table: "StudentReadingProgresses",
                columns: new[] { "StudentUserId", "BookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentReadingProgresses_TenantId",
                table: "StudentReadingProgresses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAnnouncements_TenantId",
                table: "TeacherAnnouncements",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "StudentAnnotationRecords");

            migrationBuilder.DropTable(
                name: "StudentBookmarks");

            migrationBuilder.DropTable(
                name: "StudentNotifications");

            migrationBuilder.DropTable(
                name: "StudentReadingProgresses");

            migrationBuilder.DropTable(
                name: "TeacherAnnouncements");
        }
    }
}
