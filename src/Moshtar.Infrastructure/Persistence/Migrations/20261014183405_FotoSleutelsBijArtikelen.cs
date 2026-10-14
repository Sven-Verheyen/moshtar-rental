using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FotoSleutelsBijArtikelen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Images",
                table: "RentalItems",
                newName: "Photos");

            // Foto's zijn voortaan sleutels in de afbeeldingenopslag; adressen van vroeger zijn er niet (geen overgangsregeling).
            migrationBuilder.Sql("""UPDATE "RentalItems" SET "Photos" = '[]'""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Photos",
                table: "RentalItems",
                newName: "Images");
        }
    }
}
