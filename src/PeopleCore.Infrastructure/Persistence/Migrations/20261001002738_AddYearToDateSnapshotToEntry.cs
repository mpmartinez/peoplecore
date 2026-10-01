using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddYearToDateSnapshotToEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "basic_earned_earlier_in_year",
                table: "payroll_run_employees",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "exempt_used_earlier_in_year",
                table: "payroll_run_employees",
                type: "numeric(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "basic_earned_earlier_in_year",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "exempt_used_earlier_in_year",
                table: "payroll_run_employees");
        }
    }
}
