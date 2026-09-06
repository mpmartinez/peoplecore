using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "city",
                table: "companies",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "logo",
                table: "companies",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pag_ibig_number",
                table: "companies",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "phil_health_number",
                table: "companies",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sss_number",
                table: "companies",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "tin",
                table: "companies",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "employee_allowances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    is_taxable = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_allowances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_compensations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    basic_salary = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    pay_frequency = table.Column<int>(type: "integer", nullable: false),
                    tax_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    dependents = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_compensations", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_compensations_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_loans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_type = table.Column<int>(type: "integer", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    monthly_deduction = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    remaining_balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_loans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_number = table.Column<string>(type: "text", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    pay_date = table.Column<DateOnly>(type: "date", nullable: false),
                    frequency = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attendance_period_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employees_missing_attendance = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phil_health_rate = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    phil_health_min_share = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    phil_health_max_share = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    pag_ibig_employee_rate = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    pag_ibig_low_employee_rate = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    pag_ibig_low_rate_threshold = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    pag_ibig_employer_rate = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    pag_ibig_max_fund_salary = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    daily_rate_factor = table.Column<decimal>(type: "numeric(8,2)", nullable: false),
                    sss_employee_rate = table.Column<decimal>(type: "numeric(8,4)", nullable: true),
                    sss_employer_rate = table.Column<decimal>(type: "numeric(8,4)", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_settings_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payroll_run_employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    days_worked = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    overtime_hours = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    holiday_days = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    include_thirteenth_month = table.Column<bool>(type: "boolean", nullable: false),
                    regular_pay = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    overtime_pay = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    holiday_pay = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    night_diff_pay = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    taxable_allowances = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    non_taxable_allowances = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    thirteenth_month = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    daily_rate = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    daily_rate_factor = table.Column<decimal>(type: "numeric(8,2)", nullable: false),
                    absence_deduction = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    tardiness_deduction = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    sss_employee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    sss_employer = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    phil_health_employee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    phil_health_employer = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    pag_ibig_employee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    pag_ibig_employer = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    withholding_tax = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    loan_deductions = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    other_deductions = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_run_employees", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_run_employees_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_run_employees_payroll_runs_payroll_run_id",
                        column: x => x.payroll_run_id,
                        principalTable: "payroll_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payroll_loan_deductions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_run_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_loan_deductions", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_loan_deductions_payroll_run_employees_payroll_run_e",
                        column: x => x.payroll_run_employee_id,
                        principalTable: "payroll_run_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_allowances_employee_id",
                table: "employee_allowances",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_compensations_employee_id",
                table: "employee_compensations",
                column: "employee_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_loans_employee_id",
                table: "employee_loans",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_loan_deductions_employee_loan_id",
                table: "payroll_loan_deductions",
                column: "employee_loan_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_loan_deductions_payroll_run_employee_id",
                table: "payroll_loan_deductions",
                column: "payroll_run_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_employees_employee_id",
                table: "payroll_run_employees",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_employees_payroll_run_id",
                table: "payroll_run_employees",
                column: "payroll_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_runs_run_number",
                table: "payroll_runs",
                column: "run_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_settings_company_id",
                table: "payroll_settings",
                column: "company_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_allowances");

            migrationBuilder.DropTable(
                name: "employee_compensations");

            migrationBuilder.DropTable(
                name: "employee_loans");

            migrationBuilder.DropTable(
                name: "payroll_loan_deductions");

            migrationBuilder.DropTable(
                name: "payroll_settings");

            migrationBuilder.DropTable(
                name: "payroll_run_employees");

            migrationBuilder.DropTable(
                name: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "city",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "logo",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "pag_ibig_number",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "phil_health_number",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "sss_number",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "tin",
                table: "companies");
        }
    }
}
