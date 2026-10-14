using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Leverpunten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AddressPoint_Latitude",
                table: "Tenants",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AddressPoint_Longitude",
                table: "Tenants",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeliveryPointConfirmed",
                table: "Reservations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "DeliveryPoint_Latitude",
                table: "Reservations",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DeliveryPoint_Longitude",
                table: "Reservations",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressPoint_Latitude",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AddressPoint_Longitude",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "DeliveryPointConfirmed",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "DeliveryPoint_Latitude",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "DeliveryPoint_Longitude",
                table: "Reservations");
        }
    }
}
