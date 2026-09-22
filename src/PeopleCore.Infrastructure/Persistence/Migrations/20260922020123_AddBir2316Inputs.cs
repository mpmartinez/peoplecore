using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeopleCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBir2316Inputs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bir2316inputs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    prev_employer_tin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    prev_employer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    prev_employer_address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    prev_employer_zip_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    item22_prev_taxable_compensation = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    item25b_prev_tax_withheld = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    item27_pera_tax_credit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    item35_de_minimis = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    item33_hazard_pay_mwe = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    statutory_min_wage_per_day = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    statutory_min_wage_per_month = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bir2316inputs", x => x.id);
                    table.ForeignKey(
                        name: "fk_bir2316inputs_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bir2316inputs_employee_id_year",
                table: "bir2316inputs",
                columns: new[] { "employee_id", "year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bir2316inputs");
        }
    }
}
