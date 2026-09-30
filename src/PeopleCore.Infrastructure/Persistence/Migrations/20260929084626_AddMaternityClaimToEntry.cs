using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaternityClaimToEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "maternity_claim_id",
                table: "payroll_run_employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_employees_maternity_claim_id",
                table: "payroll_run_employees",
                column: "maternity_claim_id");

            migrationBuilder.AddForeignKey(
                name: "fk_payroll_run_employees_maternity_claims_maternity_claim_id",
                table: "payroll_run_employees",
                column: "maternity_claim_id",
                principalTable: "maternity_claims",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payroll_run_employees_maternity_claims_maternity_claim_id",
                table: "payroll_run_employees");

            migrationBuilder.DropIndex(
                name: "ix_payroll_run_employees_maternity_claim_id",
                table: "payroll_run_employees");

            migrationBuilder.DropColumn(
                name: "maternity_claim_id",
                table: "payroll_run_employees");
        }
    }
}
