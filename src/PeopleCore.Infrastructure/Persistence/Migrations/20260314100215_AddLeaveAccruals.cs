using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveAccruals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leave_accrual_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenure_months_min = table.Column<int>(type: "integer", nullable: false),
                    tenure_months_max = table.Column<int>(type: "integer", nullable: true),
                    days_per_year = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    accrual_frequency = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_accrual_policies", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_accrual_policies_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_accrual_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accrual_date = table.Column<DateOnly>(type: "date", nullable: false),
                    days_accrued = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    policy_snapshot = table.Column<string>(type: "text", nullable: false),
                    period_year = table.Column<int>(type: "integer", nullable: false),
                    period_month = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_accrual_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_accrual_transactions_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_accrual_transactions_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_accrual_policies_leave_type_id",
                table: "leave_accrual_policies",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_accrual_transactions_employee_id_leave_type_id_period",
                table: "leave_accrual_transactions",
                columns: new[] { "employee_id", "leave_type_id", "period_year", "period_month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_accrual_transactions_leave_type_id",
                table: "leave_accrual_transactions",
                column: "leave_type_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leave_accrual_policies");

            migrationBuilder.DropTable(
                name: "leave_accrual_transactions");
        }
    }
}
