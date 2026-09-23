using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarkVacationLeaveConvertible : Migration
    {
        /// <summary>
        /// Marks the leave types coded VL or SIL (however the code was cased or padded) as paid out
        /// on final pay. Unused Service Incentive Leave is commutable to cash (Labor Code Art. 95),
        /// and a company's VL usually stands in for it; leave types existed before they said whether
        /// they convert, and the app has no page to set it. Every other type, and every type's
        /// CountsAsVacationForDeMinimis, is left as it is. Idempotent. Kept as a constant so
        /// MarkVacationLeaveConvertibleTests can run it against a database with rows in it.
        /// </summary>
        public const string MarkConvertibleSql = """
            UPDATE leave_types SET is_convertible_to_cash = TRUE
            WHERE upper(btrim(code)) IN ('VL', 'SIL') AND NOT is_convertible_to_cash;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MarkConvertibleSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo safely: once this has run, HR may have set a type's conversion by hand,
            // and nothing records which values came from here and which from them.
        }
    }
}
