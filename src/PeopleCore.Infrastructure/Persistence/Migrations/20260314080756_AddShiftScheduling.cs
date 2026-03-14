using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rotating_patterns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cycle_length_days = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rotating_patterns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shift_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    break_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_night_shift = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_shift_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shift_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rotating_pattern_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pattern_start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_shift_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_shift_assignments_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_shift_assignments_rotating_patterns_rotating_patte",
                        column: x => x.rotating_pattern_id,
                        principalTable: "rotating_patterns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_employee_shift_assignments_shift_templates_shift_template_id",
                        column: x => x.shift_template_id,
                        principalTable: "shift_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "rotating_pattern_slots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rotating_pattern_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_offset = table.Column<int>(type: "integer", nullable: false),
                    shift_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rotating_pattern_slots", x => x.id);
                    table.ForeignKey(
                        name: "fk_rotating_pattern_slots_rotating_patterns_rotating_pattern_id",
                        column: x => x.rotating_pattern_id,
                        principalTable: "rotating_patterns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rotating_pattern_slots_shift_templates_shift_template_id",
                        column: x => x.shift_template_id,
                        principalTable: "shift_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_shift_assignments_employee_id",
                table: "employee_shift_assignments",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_shift_assignments_rotating_pattern_id",
                table: "employee_shift_assignments",
                column: "rotating_pattern_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_shift_assignments_shift_template_id",
                table: "employee_shift_assignments",
                column: "shift_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_rotating_pattern_slots_rotating_pattern_id",
                table: "rotating_pattern_slots",
                column: "rotating_pattern_id");

            migrationBuilder.CreateIndex(
                name: "ix_rotating_pattern_slots_shift_template_id",
                table: "rotating_pattern_slots",
                column: "shift_template_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_shift_assignments");

            migrationBuilder.DropTable(
                name: "rotating_pattern_slots");

            migrationBuilder.DropTable(
                name: "rotating_patterns");

            migrationBuilder.DropTable(
                name: "shift_templates");
        }
    }
}
