using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPremiumDayUnworkedDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "unworked_days",
                table: "payroll_run_premium_days",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "unworked_days",
                table: "payroll_run_premium_days");
        }
    }
}
