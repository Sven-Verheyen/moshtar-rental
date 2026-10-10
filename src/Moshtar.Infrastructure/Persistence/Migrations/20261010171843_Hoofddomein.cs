using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Hoofddomein : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                table: "TenantHosts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_TenantHosts_PrimaryHost",
                table: "TenantHosts",
                column: "TenantId",
                unique: true,
                filter: "\"IsPrimary\"");

            // Bestaande databases: hopsakee.fun wordt het hoofddomein van Hopsakee.fun.
            migrationBuilder.Sql("UPDATE \"TenantHosts\" SET \"IsPrimary\" = true WHERE \"Hostname\" = 'hopsakee.fun';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantHosts_PrimaryHost",
                table: "TenantHosts");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                table: "TenantHosts");
        }
    }
}
