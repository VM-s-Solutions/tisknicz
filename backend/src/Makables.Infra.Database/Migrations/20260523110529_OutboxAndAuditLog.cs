using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class OutboxAndAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_audit_log",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    admin_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_entity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    before_json = table.Column<string>(type: "jsonb", nullable: true),
                    after_json = table.Column<string>(type: "jsonb", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_event",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    aggregate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    next_retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_event", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_log_action_code_created_at",
                table: "admin_audit_log",
                columns: new[] { "action_code", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_log_admin_user_id_created_at",
                table: "admin_audit_log",
                columns: new[] { "admin_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_log_target_created_at",
                table: "admin_audit_log",
                columns: new[] { "target_entity", "target_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_event_aggregate_id",
                table: "outbox_event",
                column: "aggregate_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_event_due",
                table: "outbox_event",
                columns: new[] { "next_retry_at", "processed_at" },
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_event_event_type",
                table: "outbox_event",
                column: "event_type");

            // === Append-only enforcement for admin_audit_log per ADR 0014. ===
            // Application-layer convention is reinforced at the database
            // layer: UPDATE and DELETE on admin_audit_log raise an
            // exception. The only way to "modify" an audit entry is to
            // disable the trigger as a superuser — outside normal app code.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION admin_audit_log_reject_modification()
                RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION
                        'admin_audit_log is append-only (ADR 0014). Operation % rejected.',
                        TG_OP;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_admin_audit_log_reject_update
                BEFORE UPDATE ON admin_audit_log
                FOR EACH ROW EXECUTE FUNCTION admin_audit_log_reject_modification();

                CREATE TRIGGER trg_admin_audit_log_reject_delete
                BEFORE DELETE ON admin_audit_log
                FOR EACH ROW EXECUTE FUNCTION admin_audit_log_reject_modification();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_admin_audit_log_reject_delete ON admin_audit_log;
                DROP TRIGGER IF EXISTS trg_admin_audit_log_reject_update ON admin_audit_log;
                DROP FUNCTION IF EXISTS admin_audit_log_reject_modification();
            ");

            migrationBuilder.DropTable(
                name: "admin_audit_log");

            migrationBuilder.DropTable(
                name: "outbox_event");
        }
    }
}
