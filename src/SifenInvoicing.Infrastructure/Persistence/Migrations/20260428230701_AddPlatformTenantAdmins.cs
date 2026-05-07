using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SifenInvoicing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformTenantAdmins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PlatformUsers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUsers_TenantId",
                table: "PlatformUsers",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlatformUsers_Tenants_TenantId",
                table: "PlatformUsers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlatformUsers_Tenants_TenantId",
                table: "PlatformUsers");

            migrationBuilder.DropIndex(
                name: "IX_PlatformUsers_TenantId",
                table: "PlatformUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PlatformUsers");
        }
    }
}
