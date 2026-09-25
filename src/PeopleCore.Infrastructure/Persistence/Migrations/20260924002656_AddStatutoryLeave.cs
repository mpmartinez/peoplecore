using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStatutoryLeave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "counts_calendar_days",
                table: "leave_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "days_per_event",
                table: "leave_types",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "entitlement_kind",
                table: "leave_types",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Accrued");

            migrationBuilder.AddColumn<bool>(
                name: "is_confidential",
                table: "leave_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_maternity",
                table: "leave_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "max_events",
                table: "leave_types",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "min_service_months",
                table: "leave_types",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_married",
                table: "leave_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "requires_solo_parent_id",
                table: "leave_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "days_allocated_to_father",
                table: "leave_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "days_in_start_year",
                table: "leave_requests",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "document_content_type",
                table: "leave_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_file_name",
                table: "leave_requests",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "document_size_bytes",
                table: "leave_requests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_storage_key",
                table: "leave_requests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "document_uploaded_at",
                table: "leave_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "document_uploaded_by",
                table: "leave_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "maternity_case",
                table: "leave_requests",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "solo_parent_id_number",
                table: "employees",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "solo_parent_id_valid_until",
                table: "employees",
                type: "date",
                nullable: true);

            // Every existing request already had its full TotalDays charged to the start year's
            // balance (see LeaveRequestService), whether or not start and end fell in the same
            // calendar year. Backfilling days_in_start_year = total_days for all of them matches
            // that behaviour unchanged.
            migrationBuilder.Sql(
                """
                UPDATE leave_requests
                SET days_in_start_year = total_days;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "counts_calendar_days",
                table: "leave_types");

            migrationBuilder.DropColumn(
                name: "days_per_event",
                table: "leave_types");

            migrationBuilder.DropColumn(
                name: "entitlement_kind",
                table: "leave_types");

            migrationBuilder.DropColumn(
                name: "is_confidential",
                table: "leave_types");

            migrationBuilder.DropColumn(
                name: "is_maternity",
                table: "leave_types");

            migrationBuilder.DropColumn(
                name: "max_events",
                table: "leave_types");

            migrationBuilder.DropColumn(
                name: "min_service_months",
                table: "leave_types");

            migrationBuilder.DropColumn(
                name: "requires_married",
                table: "leave_types");

            migrationBuilder.DropColumn(
                name: "requires_solo_parent_id",
                table: "leave_types");

            migrationBuilder.DropColumn(
                name: "days_allocated_to_father",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "days_in_start_year",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "document_content_type",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "document_file_name",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "document_size_bytes",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "document_storage_key",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "document_uploaded_at",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "document_uploaded_by",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "maternity_case",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "solo_parent_id_number",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "solo_parent_id_valid_until",
                table: "employees");
        }
    }
}
