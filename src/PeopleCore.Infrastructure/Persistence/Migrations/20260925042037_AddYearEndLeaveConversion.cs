using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddYearEndLeaveConversion : Migration
    {
        /// <summary>
        /// Turns on ConvertsAtYearEnd for the leave type coded SIL (however the code was cased or
        /// padded): unused Service Incentive Leave is commutable to cash once a year (Labor Code
        /// Art. 95), and the app defaults that payout to the December payroll. Idempotent. Kept as
        /// a constant so YearEndLeaveConversionStorageTests can run it against a database with rows
        /// in it.
        /// </summary>
        public const string MarkSilConvertsAtYearEndSql = """
            UPDATE leave_types SET converts_at_year_end = TRUE WHERE upper(btrim(code)) = 'SIL';
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "includes_leave_conversion",
                table: "payroll_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "converts_at_year_end",
                table: "leave_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(MarkSilConvertsAtYearEndSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "includes_leave_conversion",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "converts_at_year_end",
                table: "leave_types");
        }
    }
}
