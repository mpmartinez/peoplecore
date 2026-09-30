using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaternityDifferential : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "contributions_deferred",
                table: "payroll_run_employees",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "deferred_contributions_collected",
                table: "payroll_run_employees",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "maternity_differential",
                table: "payroll_run_employees",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "contributions_deferred",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "deferred_contributions_collected",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "maternity_differential",
                table: "payroll_run_employees");
        }
    }
}
