using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestrictLeaveTypeDeletes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_leave_balances_leave_types_leave_type_id",
                table: "leave_balances");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_requests_leave_types_leave_type_id",
                table: "leave_requests");

            migrationBuilder.AddForeignKey(
                name: "fk_leave_balances_leave_types_leave_type_id",
                table: "leave_balances",
                column: "leave_type_id",
                principalTable: "leave_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_leave_requests_leave_types_leave_type_id",
                table: "leave_requests",
                column: "leave_type_id",
                principalTable: "leave_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_leave_balances_leave_types_leave_type_id",
                table: "leave_balances");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_requests_leave_types_leave_type_id",
                table: "leave_requests");

            migrationBuilder.AddForeignKey(
                name: "fk_leave_balances_leave_types_leave_type_id",
                table: "leave_balances",
                column: "leave_type_id",
                principalTable: "leave_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_leave_requests_leave_types_leave_type_id",
                table: "leave_requests",
                column: "leave_type_id",
                principalTable: "leave_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
