using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VervangingenInPakketlijnen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReservationLineReplacements",
                columns: table => new
                {
                    RentalItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    RentalItemName = table.Column<string>(type: "text", nullable: false),
                    ReplacementRentalItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplacementName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationLineReplacements", x => new { x.ReservationLineId, x.RentalItemId });
                    table.ForeignKey(
                        name: "FK_ReservationLineReplacements_RentalItems_RentalItemId",
                        column: x => x.RentalItemId,
                        principalTable: "RentalItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReservationLineReplacements_RentalItems_ReplacementRentalIt~",
                        column: x => x.ReplacementRentalItemId,
                        principalTable: "RentalItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReservationLineReplacements_ReservationLines_ReservationLin~",
                        column: x => x.ReservationLineId,
                        principalTable: "ReservationLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationLineReplacements_RentalItemId",
                table: "ReservationLineReplacements",
                column: "RentalItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationLineReplacements_ReplacementRentalItemId",
                table: "ReservationLineReplacements",
                column: "ReplacementRentalItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationLineReplacements");
        }
    }
}
