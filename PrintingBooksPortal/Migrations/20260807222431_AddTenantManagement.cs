using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintingBooksPortal.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Tenants table must exist BEFORE the DEFAULT 1 backfill columns (§12.2 order matters)
            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MaxShops = table.Column<int>(type: "int", nullable: true),
                    MaxBooks = table.Column<int>(type: "int", nullable: true),
                    Plan = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            // 2. Explicit Id=1 so the DEFAULT 1 backfill of existing rows points at the right tenant (§12.2)
            migrationBuilder.InsertData(
                table: "Tenants",
                columns: new[] { "Id", "Name", "IsActive", "CreatedAt" },
                values: new object[] { 1, "Default Tenant", true, DateTime.UtcNow });

            // 3. Per-tenant unique key replaces the global one
            migrationBuilder.DropIndex(
                name: "IX_SystemSettings_Key",
                table: "SystemSettings");

            // 4. TenantId columns on all tenant-scoped tables (default 1 = default tenant; backfill §12.3).
            //    SystemSettings stays NULLABLE so global (SystemAdmin, tenantId<=0) rows can be stored with NULL.
            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "SystemSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Shops",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ShopBookAssignments",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "PrintLogs",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "EducationalBoards",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Books",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            // 5. TenantApiKeys (FK to Tenants — safe now that Tenants exists)
            migrationBuilder.CreateTable(
                name: "TenantApiKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    KeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantApiKeys_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // 6. Indexes on TenantId (query performance)
            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_TenantId",
                table: "SystemSettings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_TenantId_Key",
                table: "SystemSettings",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shops_TenantId",
                table: "Shops",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopBookAssignments_TenantId",
                table: "ShopBookAssignments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintLogs_TenantId",
                table: "PrintLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EducationalBoards_TenantId",
                table: "EducationalBoards",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Books_TenantId",
                table: "Books",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantApiKeys_KeyHash",
                table: "TenantApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantApiKeys_TenantId",
                table: "TenantApiKeys",
                column: "TenantId");

            // 7. Foreign keys (rows already backfilled — FK validation passes).
            //    NOTE: On SQL Server these TenantId FKs must NOT cascade where another
            //    cascade path already exists (e.g. Books -> Boards -> Tenants, and
            //    ShopBookAssignments -> Shops -> Tenants), otherwise the engine
            //    rejects "multiple cascade paths". NO ACTION is used there.
            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Tenants_TenantId",
                table: "AspNetUsers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Books_Tenants_TenantId",
                table: "Books",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EducationalBoards_Tenants_TenantId",
                table: "EducationalBoards",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PrintLogs_Tenants_TenantId",
                table: "PrintLogs",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ShopBookAssignments_Tenants_TenantId",
                table: "ShopBookAssignments",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Shops_Tenants_TenantId",
                table: "Shops",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SystemSettings_Tenants_TenantId",
                table: "SystemSettings",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // 8. Roles (SQL Server production only — the DbSeeder creates them idempotently on any
            //    provider including SQLite, so this is belt-and-suspenders §12.2 step 10)
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(@"
INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
SELECT NEWID(), 'SystemAdmin', 'SYSTEMADMIN', NEWID()
WHERE NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [Name] = 'SystemAdmin');
INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
SELECT NEWID(), 'Teacher', 'TEACHER', NEWID()
WHERE NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [Name] = 'Teacher');");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Tenants_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Books_Tenants_TenantId",
                table: "Books");

            migrationBuilder.DropForeignKey(
                name: "FK_EducationalBoards_Tenants_TenantId",
                table: "EducationalBoards");

            migrationBuilder.DropForeignKey(
                name: "FK_PrintLogs_Tenants_TenantId",
                table: "PrintLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_ShopBookAssignments_Tenants_TenantId",
                table: "ShopBookAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_Shops_Tenants_TenantId",
                table: "Shops");

            migrationBuilder.DropForeignKey(
                name: "FK_SystemSettings_Tenants_TenantId",
                table: "SystemSettings");

            migrationBuilder.DropTable(
                name: "TenantApiKeys");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_SystemSettings_TenantId",
                table: "SystemSettings");

            migrationBuilder.DropIndex(
                name: "IX_SystemSettings_TenantId_Key",
                table: "SystemSettings");

            migrationBuilder.DropIndex(
                name: "IX_Shops_TenantId",
                table: "Shops");

            migrationBuilder.DropIndex(
                name: "IX_ShopBookAssignments_TenantId",
                table: "ShopBookAssignments");

            migrationBuilder.DropIndex(
                name: "IX_PrintLogs_TenantId",
                table: "PrintLogs");

            migrationBuilder.DropIndex(
                name: "IX_EducationalBoards_TenantId",
                table: "EducationalBoards");

            migrationBuilder.DropIndex(
                name: "IX_Books_TenantId",
                table: "Books");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ShopBookAssignments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PrintLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EducationalBoards");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_Key",
                table: "SystemSettings",
                column: "Key",
                unique: true);
        }
    }
}