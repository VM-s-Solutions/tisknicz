using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <summary>
    /// T-0145 — adds the <c>disputes.auto_escalated_at</c> idempotency
    /// stamp (null until the daily 7-day maker-response sweep fires the
    /// <c>dispute.autoEscalated.adminEmail</c> notification for that
    /// dispute) + a partial index backing the sweep's candidate query
    /// (<c>resolved_at IS NULL AND source = Customer (0) AND
    /// auto_escalated_at IS NULL</c>, ordered by <c>created_at</c>).
    /// </summary>
    public partial class AddDisputeAutoEscalatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "auto_escalated_at",
                table: "disputes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_disputes_auto_escalation_candidates",
                table: "disputes",
                column: "created_at",
                filter: "resolved_at IS NULL AND source = 0 AND auto_escalated_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_disputes_auto_escalation_candidates",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "auto_escalated_at",
                table: "disputes");
        }
    }
}
