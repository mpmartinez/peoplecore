using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinalPay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "final_pay_run_id",
                table: "separations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_undone_at",
                table: "separation_clearance_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_undone_by",
                table: "separation_clearance_items",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_undone_note",
                table: "separation_clearance_items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "run_type",
                table: "payroll_runs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Regular");

            migrationBuilder.AddColumn<decimal>(
                name: "final_pay_non_taxable",
                table: "payroll_run_employees",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "leave_conversion_non_taxable",
                table: "payroll_run_employees",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "leave_conversion_pay",
                table: "payroll_run_employees",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "retirement_pay",
                table: "payroll_run_employees",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "separation_pay",
                table: "payroll_run_employees",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "counts_as_vacation_for_de_minimis",
                table: "leave_types",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_convertible_to_cash",
                table: "leave_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "final_pay_inputs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    separation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    working_days = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    separation_pay_override = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    retirement_pay_override = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    override_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_final_pay_inputs", x => x.id);
                    table.ForeignKey(
                        name: "fk_final_pay_inputs_payroll_runs_payroll_run_id",
                        column: x => x.payroll_run_id,
                        principalTable: "payroll_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "final_pay_deductions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    final_pay_inputs_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_final_pay_deductions", x => x.id);
                    table.ForeignKey(
                        name: "fk_final_pay_deductions_final_pay_inputs_final_pay_inputs_id",
                        column: x => x.final_pay_inputs_id,
                        principalTable: "final_pay_inputs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_separations_final_pay_run_id",
                table: "separations",
                column: "final_pay_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_final_pay_deductions_final_pay_inputs_id",
                table: "final_pay_deductions",
                column: "final_pay_inputs_id");

            migrationBuilder.CreateIndex(
                name: "ix_final_pay_inputs_payroll_run_id",
                table: "final_pay_inputs",
                column: "payroll_run_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_separations_payroll_runs_final_pay_run_id",
                table: "separations",
                column: "final_pay_run_id",
                principalTable: "payroll_runs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_separations_payroll_runs_final_pay_run_id",
                table: "separations");

            migrationBuilder.DropTable(
                name: "final_pay_deductions");

            migrationBuilder.DropTable(
                name: "final_pay_inputs");

            migrationBuilder.DropIndex(
                name: "ix_separations_final_pay_run_id",
                table: "separations");

            migrationBuilder.DropColumn(
                name: "final_pay_run_id",
                table: "separations");

            migrationBuilder.DropColumn(
                name: "last_undone_at",
                table: "separation_clearance_items");

            migrationBuilder.DropColumn(
                name: "last_undone_by",
                table: "separation_clearance_items");

            migrationBuilder.DropColumn(
                name: "last_undone_note",
                table: "separation_clearance_items");

            migrationBuilder.DropColumn(
                name: "run_type",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "final_pay_non_taxable",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "leave_conversion_non_taxable",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "leave_conversion_pay",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "retirement_pay",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "separation_pay",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "counts_as_vacation_for_de_minimis",
                table: "leave_types");

            migrationBuilder.DropColumn(
                name: "is_convertible_to_cash",
                table: "leave_types");
        }
    }
}
