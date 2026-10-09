using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HistoriekReservatie : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReservationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReservationEvents_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReservationEvents_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationEvents_ReservationId_OccurredAtUtc",
                table: "ReservationEvents",
                columns: new[] { "ReservationId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationEvents_TenantId",
                table: "ReservationEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationEvents_UserId",
                table: "ReservationEvents",
                column: "UserId");

            // Bestaande reservaties kwamen allemaal via de reservatiewebsite binnen.
            migrationBuilder.Sql("""
                INSERT INTO "ReservationEvents" ("Id", "ReservationId", "Kind", "Status", "OccurredAtUtc", "UserId", "TenantId")
                SELECT gen_random_uuid(), "Id", 1, 1, "CreatedAtUtc", NULL, "TenantId" FROM "Reservations";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationEvents");
        }
    }
}
