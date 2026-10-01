using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollOpeningBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_opening_balances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    through_date = table.Column<DateOnly>(type: "date", nullable: false),
                    basic_salary = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    thirteenth_month_paid = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    other_benefits_paid = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    other_taxable_pay = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    de_minimis = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    other_non_taxable = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    employee_contributions = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    tax_withheld = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    de_minimis_leave_days = table.Column<decimal>(type: "numeric(6,2)", nullable: false, defaultValue: 0m),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_opening_balances", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_opening_balances_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_opening_balances_employee_id_year",
                table: "payroll_opening_balances",
                columns: new[] { "employee_id", "year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_opening_balances");
        }
    }
}
