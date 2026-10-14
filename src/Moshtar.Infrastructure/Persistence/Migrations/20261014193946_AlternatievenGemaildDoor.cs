using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moshtar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlternatievenGemaildDoor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MailedByUserId",
                table: "AlternativeProposals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AlternativeProposals_MailedByUserId",
                table: "AlternativeProposals",
                column: "MailedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AlternativeProposals_AspNetUsers_MailedByUserId",
                table: "AlternativeProposals",
                column: "MailedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AlternativeProposals_AspNetUsers_MailedByUserId",
                table: "AlternativeProposals");

            migrationBuilder.DropIndex(
                name: "IX_AlternativeProposals_MailedByUserId",
                table: "AlternativeProposals");

            migrationBuilder.DropColumn(
                name: "MailedByUserId",
                table: "AlternativeProposals");
        }
    }
}
