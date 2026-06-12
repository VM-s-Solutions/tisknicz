using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <summary>
    /// T-0106 — dispute domain:
    /// <list type="bullet">
    ///   <item><description><c>disputes</c> table (Q2 child entity:
    ///     category / description / source / resolution outcome + notes;
    ///     SMALLINT enum columns per the <c>: short</c> convention) with
    ///     FK → orders CASCADE (GDPR hard-delete only) and the partial
    ///     unique index <c>ux_disputes_order_open UNIQUE (order_id)
    ///     WHERE resolved_at IS NULL</c> — at most one OPEN dispute per
    ///     order (§C.4; makes the concurrent-open race safe).</description></item>
    ///   <item><description><c>orders.pre_dispute_state</c> (SMALLINT
    ///     NULL) — the parenthesis-state restore pointer (patterns
    ///     §A.22); non-null ⇔ state == Disputed.</description></item>
    ///   <item><description>Seed <c>OrderDisputedAdmin</c> +
    ///     <c>OrderDisputeResolvedCustomer</c> email templates + cs-CZ +
    ///     en-US translations (T-0067 seed-in-migration precedent;
    ///     SendGrid ids are placeholders overridden post-deploy).</description></item>
    /// </list>
    /// </summary>
    public partial class AddDisputeTableAndPreDisputeState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "pre_dispute_state",
                table: "orders",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "disputes",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    order_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    category = table.Column<short>(type: "smallint", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    resolution_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_outcome = table.Column<short>(type: "smallint", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_disputes", x => x.id);
                    table.ForeignKey(
                        name: "FK_disputes_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_disputes_order_open",
                table: "disputes",
                column: "order_id",
                unique: true,
                filter: "resolved_at IS NULL");

            // === T-0106: seed dispute email templates + translations. ===
            // Mirrors the T-0105 / T-0067 seed pattern. Subjects stored
            // with double-brace placeholders so the subject substitution
            // fires (EmailSendService substitutes {{key}}).
            var seededAt = new DateTimeOffset(2026, 6, 12, 0, 0, 0, TimeSpan.Zero);
            var seededAtSql = seededAt.ToString("yyyy-MM-dd HH:mm:sszzz");

            const string disputedAdminCs =
                "Nová reklamace k objednávce {{order_number}}.\n\n" +
                "Kategorie: {{category}}\n" +
                "Zdroj: {{source}}\n" +
                "Popis: {{description}}\n\n" +
                "Detail objednávky: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string disputedAdminEn =
                "A new dispute was opened on order {{order_number}}.\n\n" +
                "Category: {{category}}\n" +
                "Source: {{source}}\n" +
                "Description: {{description}}\n\n" +
                "Order detail: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string resolvedCustomerCs =
                "Dobrý den {{contact_name}},\n\n" +
                "vaše reklamace k objednávce {{order_number}} byla vyřízena — výsledkem je {{outcome_label}}.\n\n" +
                "Vyjádření týmu Makables:\n{{resolution_notes}}\n\n" +
                "Detail objednávky: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string resolvedCustomerEn =
                "Hi {{contact_name}},\n\n" +
                "your dispute on order {{order_number}} has been resolved — outcome: {{outcome}}.\n\n" +
                "Note from the Makables team:\n{{resolution_notes}}\n\n" +
                "Order detail: {{action_url}}\n\n" +
                "Makables — makables.cz";

            migrationBuilder.Sql($@"
                INSERT INTO email_templates
                    (id, type, provider_template_id, from_address, from_name, reply_to_address,
                     is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-order-disputed-admin',            'OrderDisputedAdmin',           'd-placeholder-order-disputed-admin',            NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-order-dispute-resolved-customer', 'OrderDisputeResolvedCustomer', 'd-placeholder-order-dispute-resolved-customer', NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}');

                INSERT INTO email_template_translations
                    (id, email_template_id, language_code, subject, plain_text_body, is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-tr-order-disputed-admin-cs',            'tpl-order-disputed-admin',            'cs-CZ', 'Nová reklamace k objednávce #{{{{order_number}}}}',      {QuoteSql(disputedAdminCs)},    TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-order-disputed-admin-en',            'tpl-order-disputed-admin',            'en-US', 'New dispute on order #{{{{order_number}}}}',             {QuoteSql(disputedAdminEn)},    TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-dispute-resolved-customer-cs', 'tpl-order-dispute-resolved-customer', 'cs-CZ', 'Vaše reklamace k objednávce #{{{{order_number}}}} byla vyřízena', {QuoteSql(resolvedCustomerCs)}, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-dispute-resolved-customer-en', 'tpl-order-dispute-resolved-customer', 'en-US', 'Your dispute on order #{{{{order_number}}}} was resolved',        {QuoteSql(resolvedCustomerEn)}, TRUE, 'CZ', 'seed', '{seededAtSql}');
            ");
        }

        private static string QuoteSql(string value) =>
            $"'{value.Replace("'", "''")}'";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse order: drop seed rows, then schema changes.
            migrationBuilder.Sql(@"
                DELETE FROM email_template_translations
                WHERE id IN (
                    'tpl-tr-order-disputed-admin-cs',
                    'tpl-tr-order-disputed-admin-en',
                    'tpl-tr-dispute-resolved-customer-cs',
                    'tpl-tr-dispute-resolved-customer-en'
                );

                DELETE FROM email_templates
                WHERE id IN (
                    'tpl-order-disputed-admin',
                    'tpl-order-dispute-resolved-customer'
                );
            ");

            migrationBuilder.DropTable(
                name: "disputes");

            migrationBuilder.DropColumn(
                name: "pre_dispute_state",
                table: "orders");
        }
    }
}
