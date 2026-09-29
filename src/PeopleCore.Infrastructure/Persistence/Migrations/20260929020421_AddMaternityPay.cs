using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaternityPay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "exempt_from_maternity_differential",
                table: "payroll_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "advance_maternity_benefit",
                table: "payroll_run_employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "maternity_benefit_advance",
                table: "payroll_run_employees",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "maternity_benefit_offset",
                table: "payroll_run_employees",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "maternity_claims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    daily_allowance = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    days = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    benefit = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    advance_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    advanced_at = table.Column<DateOnly>(type: "date", nullable: true),
                    reimbursed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    reimbursed_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maternity_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_maternity_claims_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maternity_claims_leave_requests_leave_request_id",
                        column: x => x.leave_request_id,
                        principalTable: "leave_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maternity_claims_payroll_runs_advance_run_id",
                        column: x => x.advance_run_id,
                        principalTable: "payroll_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_maternity_claims_advance_run_id",
                table: "maternity_claims",
                column: "advance_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_maternity_claims_employee_id",
                table: "maternity_claims",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_maternity_claims_leave_request_id",
                table: "maternity_claims",
                column: "leave_request_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maternity_claims");

            migrationBuilder.DropColumn(
                name: "exempt_from_maternity_differential",
                table: "payroll_settings");

            migrationBuilder.DropColumn(
                name: "advance_maternity_benefit",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "maternity_benefit_advance",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "maternity_benefit_offset",
                table: "payroll_run_employees");
        }
    }
}
