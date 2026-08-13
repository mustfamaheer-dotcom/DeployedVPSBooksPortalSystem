using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintingBooksPortal.Migrations
{
    /// <inheritdoc />
    public partial class AddShopApiKeysAndRegisteredPrinters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SystemSettings_Tenants_TenantId",
                table: "SystemSettings");

            migrationBuilder.DropIndex(
                name: "IX_SystemSettings_TenantId_Key",
                table: "SystemSettings");

            migrationBuilder.AddColumn<int>(
                name: "ShopId",
                table: "TenantApiKeys",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TenantId",
                table: "SystemSettings",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "StoredPassword",
                table: "Shops",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pages",
                table: "PrintLogs",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RegisteredPrinters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    ShopId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Port = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ConnectionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Driver = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsOnline = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AgentKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LastSeen = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegisteredPrinters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegisteredPrinters_Shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "Shops",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RegisteredPrinters_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantApiKeys_ShopId",
                table: "TenantApiKeys",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_TenantId_Key",
                table: "SystemSettings",
                columns: new[] { "TenantId", "Key" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredPrinters_AgentKeyHash",
                table: "RegisteredPrinters",
                column: "AgentKeyHash");

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredPrinters_LastSeen",
                table: "RegisteredPrinters",
                column: "LastSeen");

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredPrinters_ShopId",
                table: "RegisteredPrinters",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredPrinters_TenantId",
                table: "RegisteredPrinters",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredPrinters_TenantId_ShopId_AgentKeyHash_Name",
                table: "RegisteredPrinters",
                columns: new[] { "TenantId", "ShopId", "AgentKeyHash", "Name" },
                unique: true,
                filter: "[ShopId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SystemSettings_Tenants_TenantId",
                table: "SystemSettings",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TenantApiKeys_Shops_ShopId",
                table: "TenantApiKeys",
                column: "ShopId",
                principalTable: "Shops",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SystemSettings_Tenants_TenantId",
                table: "SystemSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantApiKeys_Shops_ShopId",
                table: "TenantApiKeys");

            migrationBuilder.DropTable(
                name: "RegisteredPrinters");

            migrationBuilder.DropIndex(
                name: "IX_TenantApiKeys_ShopId",
                table: "TenantApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_SystemSettings_TenantId_Key",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ShopId",
                table: "TenantApiKeys");

            migrationBuilder.DropColumn(
                name: "StoredPassword",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "Pages",
                table: "PrintLogs");

            migrationBuilder.AlterColumn<int>(
                name: "TenantId",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_TenantId_Key",
                table: "SystemSettings",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SystemSettings_Tenants_TenantId",
                table: "SystemSettings",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
