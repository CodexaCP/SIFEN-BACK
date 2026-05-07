using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SifenInvoicing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentErrors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentErrors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Cdc = table.Column<string>(type: "nvarchar(44)", maxLength: 44, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ErrorCategory = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TechnicalMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    UserMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SuggestedAction = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsRetryable = table.Column<bool>(type: "bit", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RawResponse = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentErrors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentErrors_TenantId_InvoiceId_CreatedAt",
                table: "DocumentErrors",
                columns: new[] { "TenantId", "InvoiceId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentErrors");
        }
    }
}
