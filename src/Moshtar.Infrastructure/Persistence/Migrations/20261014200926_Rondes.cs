using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rondes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bestaande verhuurders krijgen de standaarden voor de rondes (RoundSettings.Default).
            migrationBuilder.AddColumn<TimeOnly>(
                name: "CollectionDeparture",
                table: "Tenants",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(18, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "DeliveryDeparture",
                table: "Tenants",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(8, 0, 0));

            migrationBuilder.AddColumn<int>(
                name: "SetupMinutes",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "TeardownMinutes",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "WindowMinutes",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.CreateTable(
                name: "Rounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Departure = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ReturnTravelMinutes = table.Column<int>(type: "integer", nullable: false),
                    Return = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rounds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoundStops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Point_Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Point_Longitude = table.Column<double>(type: "double precision", nullable: false),
                    FixedTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    HandlingMinutes = table.Column<int>(type: "integer", nullable: true),
                    TravelMinutes = table.Column<int>(type: "integer", nullable: false),
                    Arrival = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    WindowStart = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    WindowEnd = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    MailedWindowStart = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    MailedWindowEnd = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    MailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WhatsAppAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RoundId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoundStops_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoundStops_Rounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "Rounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_TenantId",
                table: "Rounds",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_TenantId_Day_Kind",
                table: "Rounds",
                columns: new[] { "TenantId", "Day", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoundStops_ReservationId",
                table: "RoundStops",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_RoundStops_RoundId",
                table: "RoundStops",
                column: "RoundId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoundStops");

            migrationBuilder.DropTable(
                name: "Rounds");

            migrationBuilder.DropColumn(
                name: "CollectionDeparture",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "DeliveryDeparture",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SetupMinutes",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TeardownMinutes",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "WindowMinutes",
                table: "Tenants");
        }
    }
}
