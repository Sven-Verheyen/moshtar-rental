using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Prijsregels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PricingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Percentage = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    AppliesToAll = table.Column<bool>(type: "boolean", nullable: false),
                    CategoryIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    RentalItemIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    BundleIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    MinimumQuantity = table.Column<int>(type: "integer", nullable: true),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: true),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    Weekdays = table.Column<int[]>(type: "integer[]", nullable: false),
                    ReservedFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ReservedUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    RentalFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    RentalUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Names = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PricingRules_TenantId",
                table: "PricingRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PricingRules_TenantId_Kind",
                table: "PricingRules",
                columns: new[] { "TenantId", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PricingRules");
        }
    }
}
