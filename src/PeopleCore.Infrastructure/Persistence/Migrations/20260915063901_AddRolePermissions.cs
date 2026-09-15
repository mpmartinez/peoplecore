using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolePermissions : Migration
    {
        /// <summary>
        /// Gives roles that already exist the permissions matching what the hard-coded checks allowed,
        /// and marks the system roles. Runs once, as part of this migration; a fresh database has no
        /// roles yet at this point and gets them from RoleSeeder instead. Kept in step with
        /// SeededRoles by RoleSeedingTests. Idempotent, so running it twice changes nothing.
        /// </summary>
        public const string SeedExistingRolesSql = """
            INSERT INTO "AspNetRoleClaims" (role_id, claim_type, claim_value)
            SELECT r.id, 'permission', p.key
            FROM "AspNetRoles" r
            JOIN (VALUES
                ('HRMANAGER', 'employees.view-all'),
                ('HRMANAGER', 'employees.manage'),
                ('HRMANAGER', 'organization.manage'),
                ('HRMANAGER', 'attendance.manage'),
                ('HRMANAGER', 'attendance.device-sync'),
                ('HRMANAGER', 'leave.manage'),
                ('HRMANAGER', 'approvals.all'),
                ('HRMANAGER', 'performance.manage'),
                ('HRMANAGER', 'payroll.manage'),
                ('HRMANAGER', 'recruitment.manage'),
                ('HRMANAGER', 'scheduling.manage'),
                ('HRMANAGER', 'analytics.hr'),
                ('HRMANAGER', 'users.manage'),
                ('MANAGER', 'approvals.team'),
                ('PAYROLLSERVICE', 'payroll.manage'),
                ('SERVICE', 'attendance.device-sync')
            ) AS p(role, key) ON r.normalized_name = p.role
            WHERE NOT EXISTS (
                SELECT 1 FROM "AspNetRoleClaims" c
                WHERE c.role_id = r.id AND c.claim_type = 'permission' AND c.claim_value = p.key);

            UPDATE "AspNetRoles" SET is_system = TRUE WHERE normalized_name IN ('ADMIN', 'EMPLOYEE', 'SERVICE');

            UPDATE "AspNetRoles" SET description = CASE normalized_name
                WHEN 'ADMIN' THEN 'Every permission, always. Cannot be edited or deleted.'
                WHEN 'HRMANAGER' THEN 'Runs HR: employees, organisation, time, approvals, performance, payroll, recruitment, schedules, analytics and user accounts.'
                WHEN 'MANAGER' THEN 'Approves leave, overtime and performance reviews for their direct reports.'
                WHEN 'EMPLOYEE' THEN 'Self-service: their own profile, attendance, leave and payslips. Every account holds it.'
                WHEN 'PAYROLLSERVICE' THEN 'Runs payroll.'
                WHEN 'SERVICE' THEN 'Attendance devices sending clock-ins. Cannot be edited or deleted.'
            END
            WHERE description IS NULL
              AND normalized_name IN ('ADMIN', 'HRMANAGER', 'MANAGER', 'EMPLOYEE', 'PAYROLLSERVICE', 'SERVICE');
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "AspNetRoles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_system",
                table: "AspNetRoles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(SeedExistingRolesSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "description",
                table: "AspNetRoles");

            migrationBuilder.DropColumn(
                name: "is_system",
                table: "AspNetRoles");
        }
    }
}
