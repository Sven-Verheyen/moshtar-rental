using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KlantenImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CreatedByImport",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedByImport",
                table: "Customers");
        }
    }
}
