using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SifenInvoicing.Infrastructure.Persistence.Migrations
{
    public partial class AddTenantPlanLimits : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxInvoicesPerMonth",
                table: "Tenants",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxUsers",
                table: "Tenants",
                type: "int",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxInvoicesPerMonth",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "MaxUsers",
                table: "Tenants");
        }
    }
}
