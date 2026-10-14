using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AfbeeldingenVoorHuisstijl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LogoUrl",
                table: "Tenants",
                newName: "LogoImageKey");

            migrationBuilder.RenameColumn(
                name: "HeroImageUrl",
                table: "Tenants",
                newName: "HeroImageKey");

            // Tot nu toe stond hier een adres, geen sleutel uit de afbeeldingenopslag; geen overgangsregeling (ADR 0005).
            migrationBuilder.Sql("UPDATE \"Tenants\" SET \"LogoImageKey\" = NULL, \"HeroImageKey\" = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LogoImageKey",
                table: "Tenants",
                newName: "LogoUrl");

            migrationBuilder.RenameColumn(
                name: "HeroImageKey",
                table: "Tenants",
                newName: "HeroImageUrl");
        }
    }
}
