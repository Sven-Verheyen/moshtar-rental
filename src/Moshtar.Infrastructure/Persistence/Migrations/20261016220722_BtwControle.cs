using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BtwControle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VatCheck",
                table: "Customers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VatCheckedAtUtc",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatRegistryAddress",
                table: "Customers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatRegistryName",
                table: "Customers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // Bestaande btw-nummers: een EU-nummer is nog niet gecontroleerd (de achtergrondcontrole pikt het op), een
            // nummer van buiten de EU is niet te controleren. Zoals VatNumberFormat: zonder scheidingstekens, GR is EL.
            migrationBuilder.Sql("""
                UPDATE "Customers" SET "VatCheck" =
                    CASE WHEN left(upper(regexp_replace("VatNumber", '[\s./-]', '', 'g')), 2) IN
                        ('AT','BE','BG','CY','CZ','DE','DK','EE','EL','GR','ES','FI','FR','HR','HU','IE','IT','LT','LU',
                         'LV','MT','NL','PL','PT','RO','SE','SI','SK','XI')
                    THEN 'NotChecked' ELSE 'NotCheckable' END
                WHERE coalesce(trim("VatNumber"), '') <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VatCheck",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "VatCheckedAtUtc",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "VatRegistryAddress",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "VatRegistryName",
                table: "Customers");
        }
    }
}
