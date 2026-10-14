using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FotosBijPakketten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Photos",
                table: "Bundles",
                type: "jsonb",
                nullable: true);

            // Bestaande pakketten hebben nog geen eigen foto's: ze tonen die van hun artikelen.
            migrationBuilder.Sql("""UPDATE "Bundles" SET "Photos" = '[]'""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Photos",
                table: "Bundles");
        }
    }
}
