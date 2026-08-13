using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintingBooksPortal.Migrations
{
    /// <inheritdoc />
    public partial class CascadeDeleteShopKeysAndPrinters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RegisteredPrinters_Shops_ShopId",
                table: "RegisteredPrinters");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantApiKeys_Shops_ShopId",
                table: "TenantApiKeys");

            migrationBuilder.AddForeignKey(
                name: "FK_RegisteredPrinters_Shops_ShopId",
                table: "RegisteredPrinters",
                column: "ShopId",
                principalTable: "Shops",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantApiKeys_Shops_ShopId",
                table: "TenantApiKeys",
                column: "ShopId",
                principalTable: "Shops",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RegisteredPrinters_Shops_ShopId",
                table: "RegisteredPrinters");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantApiKeys_Shops_ShopId",
                table: "TenantApiKeys");

            migrationBuilder.AddForeignKey(
                name: "FK_RegisteredPrinters_Shops_ShopId",
                table: "RegisteredPrinters",
                column: "ShopId",
                principalTable: "Shops",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TenantApiKeys_Shops_ShopId",
                table: "TenantApiKeys",
                column: "ShopId",
                principalTable: "Shops",
                principalColumn: "Id");
        }
    }
}
