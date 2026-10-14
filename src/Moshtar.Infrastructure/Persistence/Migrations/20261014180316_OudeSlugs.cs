using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OudeSlugs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OldSlugs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OldSlugs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OldSlugs_TenantId",
                table: "OldSlugs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OldSlugs_TenantId_Kind_Culture_Slug",
                table: "OldSlugs",
                columns: new[] { "TenantId", "Kind", "Culture", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OldSlugs_TenantId_Kind_TargetId",
                table: "OldSlugs",
                columns: new[] { "TenantId", "Kind", "TargetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OldSlugs");
        }
    }
}
