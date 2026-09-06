using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniquePayrollSettingsCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payroll_settings_company_id",
                table: "payroll_settings");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_settings_company_id",
                table: "payroll_settings",
                column: "company_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payroll_settings_company_id",
                table: "payroll_settings");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_settings_company_id",
                table: "payroll_settings",
                column: "company_id");
        }
    }
}
