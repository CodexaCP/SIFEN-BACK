using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SifenInvoicing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fase43DeEmitterActivitiesAndLineCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProductCode",
                table: "DocumentLines",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnitCode",
                table: "DocumentLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitDescription",
                table: "DocumentLines",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaxpayerEconomicActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxpayerProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxpayerEconomicActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxpayerEconomicActivities_TaxpayerProfiles_TaxpayerProfileId",
                        column: x => x.TaxpayerProfileId,
                        principalTable: "TaxpayerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaxpayerEconomicActivities_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaxpayerEconomicActivities_TaxpayerProfileId_Code",
                table: "TaxpayerEconomicActivities",
                columns: new[] { "TaxpayerProfileId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxpayerEconomicActivities_TenantId",
                table: "TaxpayerEconomicActivities",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxpayerEconomicActivities");

            migrationBuilder.DropColumn(
                name: "ProductCode",
                table: "DocumentLines");

            migrationBuilder.DropColumn(
                name: "UnitCode",
                table: "DocumentLines");

            migrationBuilder.DropColumn(
                name: "UnitDescription",
                table: "DocumentLines");
        }
    }
}
