using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Klantenbestand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId_Email",
                table: "Customers");

            migrationBuilder.AddColumn<string>(
                name: "BillingAddress_City",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingAddress_Country",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingAddress_PostalCode",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingAddress_Street",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Customers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Customers",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "Customers",
                type: "uuid",
                nullable: true);

            // Dubbels van hetzelfde (genormaliseerde) e-mailadres per verhuurder worden één klant: die met de laatste
            // reservatie, zoals reserveren de klantfiche ook bijwerkt met de laatst ingevulde gegevens. Hun reservaties
            // gaan naar die klant.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "CustomerMerge" AS
                WITH ranked AS (
                    SELECT c."Id", c."TenantId", lower(trim(c."Email")) AS "Email",
                           row_number() OVER (
                               PARTITION BY c."TenantId", lower(trim(c."Email"))
                               ORDER BY (SELECT max(r."CreatedAtUtc") FROM "Reservations" r WHERE r."CustomerId" = c."Id") DESC NULLS LAST, c."Id"
                           ) AS "Rank"
                    FROM "Customers" c
                )
                SELECT d."Id" AS "DuplicateId", k."Id" AS "KeepId"
                FROM ranked d JOIN ranked k ON k."TenantId" = d."TenantId" AND k."Email" = d."Email" AND k."Rank" = 1
                WHERE d."Rank" > 1;

                UPDATE "Reservations" r SET "CustomerId" = m."KeepId" FROM "CustomerMerge" m WHERE r."CustomerId" = m."DuplicateId";
                DELETE FROM "Customers" c USING "CustomerMerge" m WHERE c."Id" = m."DuplicateId";
                UPDATE "Customers" SET "Email" = lower(trim("Email"));
                DROP TABLE "CustomerMerge";
                """);

            // Bestaande klanten: zakelijk als hun btw-nummer gekend is, anders particulier; factuuradres is hetzelfde.
            // Aangemaakt bij hun eerste reservatie (door wie die invoerde, of via website), gewijzigd bij hun laatste.
            migrationBuilder.Sql("""
                UPDATE "Customers" c SET
                    "Kind" = CASE WHEN coalesce(trim(c."VatNumber"), '') <> '' THEN 'Business' ELSE 'Private' END,
                    "CreatedAtUtc" = coalesce(
                        (SELECT min(r."CreatedAtUtc") FROM "Reservations" r WHERE r."CustomerId" = c."Id"), now()),
                    "CreatedByUserId" = (
                        SELECT e."UserId" FROM "Reservations" r
                        JOIN "ReservationEvents" e ON e."ReservationId" = r."Id" AND e."Kind" = 1
                        WHERE r."CustomerId" = c."Id" ORDER BY r."CreatedAtUtc" LIMIT 1),
                    "UpdatedAtUtc" = coalesce(
                        (SELECT max(r."CreatedAtUtc") FROM "Reservations" r WHERE r."CustomerId" = c."Id"), now()),
                    "UpdatedByUserId" = (
                        SELECT e."UserId" FROM "Reservations" r
                        JOIN "ReservationEvents" e ON e."ReservationId" = r."Id" AND e."Kind" = 1
                        WHERE r."CustomerId" = c."Id" ORDER BY r."CreatedAtUtc" DESC LIMIT 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_Email",
                table: "Customers",
                columns: new[] { "TenantId", "Email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId_Email",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BillingAddress_City",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BillingAddress_Country",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BillingAddress_PostalCode",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BillingAddress_Street",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_Email",
                table: "Customers",
                columns: new[] { "TenantId", "Email" });
        }
    }
}
