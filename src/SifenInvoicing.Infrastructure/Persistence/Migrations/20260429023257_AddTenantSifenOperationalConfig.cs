using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SifenInvoicing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSifenOperationalConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CertificateAlias",
                table: "TenantSifenSettings",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificatePasswordSecretReference",
                table: "TenantSifenSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateSecretReference",
                table: "TenantSifenSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentDocumentNumber",
                table: "TenantSifenSettings",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EstablishmentCode",
                table: "TenantSifenSettings",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExpeditionPointCode",
                table: "TenantSifenSettings",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StampingNumber",
                table: "TenantSifenSettings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificateAlias",
                table: "TenantSifenSettings");

            migrationBuilder.DropColumn(
                name: "CertificatePasswordSecretReference",
                table: "TenantSifenSettings");

            migrationBuilder.DropColumn(
                name: "CertificateSecretReference",
                table: "TenantSifenSettings");

            migrationBuilder.DropColumn(
                name: "CurrentDocumentNumber",
                table: "TenantSifenSettings");

            migrationBuilder.DropColumn(
                name: "EstablishmentCode",
                table: "TenantSifenSettings");

            migrationBuilder.DropColumn(
                name: "ExpeditionPointCode",
                table: "TenantSifenSettings");

            migrationBuilder.DropColumn(
                name: "StampingNumber",
                table: "TenantSifenSettings");
        }
    }
}
