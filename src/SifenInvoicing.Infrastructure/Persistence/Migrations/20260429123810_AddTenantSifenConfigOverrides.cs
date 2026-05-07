using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SifenInvoicing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSifenConfigOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EndpointUrl",
                table: "TenantSifenSettings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransportMode",
                table: "TenantSifenSettings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "XmlSchemaRootPath",
                table: "TenantSifenSettings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndpointUrl",
                table: "TenantSifenSettings");

            migrationBuilder.DropColumn(
                name: "TransportMode",
                table: "TenantSifenSettings");

            migrationBuilder.DropColumn(
                name: "XmlSchemaRootPath",
                table: "TenantSifenSettings");
        }
    }
}
