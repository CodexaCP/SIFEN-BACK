using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SifenInvoicing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantKudeTemplateSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantKudeTemplateSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PrimaryColor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SecondaryColor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FooterText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ShowPhone = table.Column<bool>(type: "bit", nullable: false),
                    ShowEmail = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantKudeTemplateSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantKudeTemplateSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantKudeTemplateSettings_TenantId",
                table: "TenantKudeTemplateSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantKudeTemplateSettings");
        }
    }
}
