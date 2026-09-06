using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceSnapshotColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "absence_days",
                table: "payroll_run_employees",
                type: "numeric(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "holiday_regular_days",
                table: "payroll_run_employees",
                type: "numeric(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "holiday_special_days",
                table: "payroll_run_employees",
                type: "numeric(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "late_minutes",
                table: "payroll_run_employees",
                type: "numeric(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "night_diff_hours",
                table: "payroll_run_employees",
                type: "numeric(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "rest_day_ot_hours",
                table: "payroll_run_employees",
                type: "numeric(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "undertime_minutes",
                table: "payroll_run_employees",
                type: "numeric(6,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "absence_days",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "holiday_regular_days",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "holiday_special_days",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "late_minutes",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "night_diff_hours",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "rest_day_ot_hours",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "undertime_minutes",
                table: "payroll_run_employees");
        }
    }
}
