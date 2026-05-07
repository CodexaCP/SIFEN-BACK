using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SifenInvoicing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTenantPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsolationMode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxpayerProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RucNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RucCheckDigit = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    TradeName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxpayerProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxpayerProfiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantCertificateMetadata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Alias = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FingerprintSha256 = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SecretReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantCertificateMetadata", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantCertificateMetadata_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantSifenSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CscIdentifier = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CscSecretReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSifenSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantSifenSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaxpayerProfiles_TenantId_RucNumber_RucCheckDigit",
                table: "TaxpayerProfiles",
                columns: new[] { "TenantId", "RucNumber", "RucCheckDigit" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantCertificateMetadata_TenantId_Environment_Purpose_Alias",
                table: "TenantCertificateMetadata",
                columns: new[] { "TenantId", "Environment", "Purpose", "Alias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantCertificateMetadata_TenantId_FingerprintSha256",
                table: "TenantCertificateMetadata",
                columns: new[] { "TenantId", "FingerprintSha256" });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantSifenSettings_TenantId_Environment",
                table: "TenantSifenSettings",
                columns: new[] { "TenantId", "Environment" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxpayerProfiles");

            migrationBuilder.DropTable(
                name: "TenantCertificateMetadata");

            migrationBuilder.DropTable(
                name: "TenantSifenSettings");

            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
