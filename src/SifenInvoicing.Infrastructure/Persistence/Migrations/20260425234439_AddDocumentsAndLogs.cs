using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SifenInvoicing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentsAndLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Cdc = table.Column<string>(type: "nvarchar(44)", maxLength: 44, nullable: false),
                    ExternalDocumentNumber = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    EstablishmentCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ExpeditionPointCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ReceiverName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReceiverDocument = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    XmlPayload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignedXmlPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastSubmissionEndpoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SifenTrackingId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StatusCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StatusMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RawSifenResponse = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinalizedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Logs_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_Cdc",
                table: "Documents",
                columns: new[] { "TenantId", "Cdc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_Environment_Kind_EstablishmentCode_ExpeditionPointCode_ExternalDocumentNumber",
                table: "Documents",
                columns: new[] { "TenantId", "Environment", "Kind", "EstablishmentCode", "ExpeditionPointCode", "ExternalDocumentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Logs_DocumentId",
                table: "Logs",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Logs_TenantId_DocumentId_CreatedAt",
                table: "Logs",
                columns: new[] { "TenantId", "DocumentId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Logs");

            migrationBuilder.DropTable(
                name: "Documents");
        }
    }
}
