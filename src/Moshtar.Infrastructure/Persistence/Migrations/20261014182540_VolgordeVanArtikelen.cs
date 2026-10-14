using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VolgordeVanArtikelen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "RentalItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Bestaande artikelen krijgen een volgorde binnen hun categorie, op slug.
            migrationBuilder.Sql("""
                UPDATE "RentalItems" AS i SET "SortOrder" = n.rank
                FROM (SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "TenantId", "CategoryId" ORDER BY "Slug") AS rank FROM "RentalItems") AS n
                WHERE i."Id" = n."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "RentalItems");
        }
    }
}
