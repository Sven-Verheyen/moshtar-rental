using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RondeVlaggenEnBedrijfspunt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Late",
                table: "RoundStops",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WindowPastMidnight",
                table: "RoundStops",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "BusinessPoint_Latitude",
                table: "Rounds",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BusinessPoint_Longitude",
                table: "Rounds",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PastMidnight",
                table: "Rounds",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Bestaande rondes: zo goed als het uit de bewaarde uren af te leiden is (zoals tot nu); "Ronde bijwerken"
            // rekent ze exact. Het bedrijfspunt is het huidige: daarmee werden ze normaal berekend.
            migrationBuilder.Sql("""
                UPDATE "Rounds" AS r SET "BusinessPoint_Latitude" = t."AddressPoint_Latitude", "BusinessPoint_Longitude" = t."AddressPoint_Longitude"
                FROM "Tenants" AS t WHERE t."Id" = r."TenantId";
                UPDATE "Rounds" SET "PastMidnight" = "Return" < "Departure";
                UPDATE "RoundStops" AS s SET
                    "Late" = s."FixedTime" IS NOT NULL AND s."Arrival" > s."FixedTime",
                    "WindowPastMidnight" = s."WindowEnd" < s."WindowStart" OR s."Arrival" < r."Departure"
                FROM "Rounds" AS r WHERE r."Id" = s."RoundId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Late",
                table: "RoundStops");

            migrationBuilder.DropColumn(
                name: "WindowPastMidnight",
                table: "RoundStops");

            migrationBuilder.DropColumn(
                name: "BusinessPoint_Latitude",
                table: "Rounds");

            migrationBuilder.DropColumn(
                name: "BusinessPoint_Longitude",
                table: "Rounds");

            migrationBuilder.DropColumn(
                name: "PastMidnight",
                table: "Rounds");
        }
    }
}
