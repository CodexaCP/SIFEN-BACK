using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SifenInvoicing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitCertificateSecretReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SecretReference",
                table: "TenantCertificateMetadata",
                newName: "CertificateSecretReference");

            migrationBuilder.AddColumn<string>(
                name: "CertificatePasswordSecretReference",
                table: "TenantCertificateMetadata",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificatePasswordSecretReference",
                table: "TenantCertificateMetadata");

            migrationBuilder.RenameColumn(
                name: "CertificateSecretReference",
                table: "TenantCertificateMetadata",
                newName: "SecretReference");
        }
    }
}
