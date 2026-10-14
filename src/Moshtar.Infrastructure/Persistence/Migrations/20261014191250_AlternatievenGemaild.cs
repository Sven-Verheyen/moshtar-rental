using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlternatievenGemaild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AlternativesMailedAtUtc",
                table: "Reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "ReservationEvents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AlternativeProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RentalItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlternativeRentalItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Surcharge = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    MailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlternativeProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlternativeProposals_RentalItems_AlternativeRentalItemId",
                        column: x => x.AlternativeRentalItemId,
                        principalTable: "RentalItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AlternativeProposals_RentalItems_RentalItemId",
                        column: x => x.RentalItemId,
                        principalTable: "RentalItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AlternativeProposals_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlternativeProposals_AlternativeRentalItemId",
                table: "AlternativeProposals",
                column: "AlternativeRentalItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AlternativeProposals_RentalItemId",
                table: "AlternativeProposals",
                column: "RentalItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AlternativeProposals_ReservationId_RentalItemId_Alternative~",
                table: "AlternativeProposals",
                columns: new[] { "ReservationId", "RentalItemId", "AlternativeRentalItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AlternativeProposals_TenantId",
                table: "AlternativeProposals",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlternativeProposals");

            migrationBuilder.DropColumn(
                name: "AlternativesMailedAtUtc",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "ReservationEvents");
        }
    }
}
