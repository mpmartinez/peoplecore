using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollRunPremiumDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_run_premium_days",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_run_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    days = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    hours = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    overtime_hours = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    night_diff_hours = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_run_premium_days", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_run_premium_days_payroll_run_employees_payroll_run_",
                        column: x => x.payroll_run_employee_id,
                        principalTable: "payroll_run_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_premium_days_payroll_run_employee_id",
                table: "payroll_run_premium_days",
                column: "payroll_run_employee_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_run_premium_days");
        }
    }
}
