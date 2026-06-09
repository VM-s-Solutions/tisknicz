using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <summary>
    /// Delivery-close bundle migration (T-0076 + T-0077 + T-0078):
    /// adds the Order <c>delivery_source SMALLINT NULL</c> column;
    /// seeds the <c>OrderDeliveredCustomer</c> email template with
    /// cs-CZ + en-US translations.
    ///
    /// <para>
    /// T-0077 + T-0078 are pure Function additions with no schema
    /// changes; they only consume T-0076's column + email template.
    /// Combined into a single migration because the three tickets ship
    /// in the same PR (delivery-close bundle) and must apply atomically.
    /// </para>
    /// </summary>
    public partial class DeliveryCloseBundle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // === T-0076 §A.1: Order.DeliverySource column. ===
            // Nullable so historical Delivered orders pre-T-0076 stay valid;
            // SMALLINT to match the OrderDeliverySource : short backing.
            migrationBuilder.AddColumn<short>(
                name: "delivery_source",
                table: "orders",
                type: "smallint",
                nullable: true);

            // === T-0076 §C: seed OrderDeliveredCustomer template + 2 translations. ===
            // Mirrors 20260606155359_SeedOrderEmailTemplates.cs §49-64 verbatim.
            // SendGrid template id is a placeholder; the real id is set via
            // App Service configuration override post-deploy.
            var seededAt = new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);
            var seededAtSql = seededAt.ToString("yyyy-MM-dd HH:mm:sszzz");

            const string deliveredCs =
                "Dobrý den {{contact_name}},\n\n" +
                "objednávka {{order_number}} byla doručena.\n" +
                "Detail najdete na: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string deliveredEn =
                "Hi {{contact_name}},\n\n" +
                "your order {{order_number}} has been delivered.\n" +
                "View details at: {{action_url}}\n\n" +
                "Makables — makables.cz";

            migrationBuilder.Sql($@"
                INSERT INTO email_templates
                    (id, type, provider_template_id, from_address, from_name, reply_to_address,
                     is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-order-delivered-customer', 'OrderDeliveredCustomer', 'd-placeholder-order-delivered-customer', NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}');

                INSERT INTO email_template_translations
                    (id, email_template_id, language_code, subject, plain_text_body, is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-tr-order-delivered-customer-cs', 'tpl-order-delivered-customer', 'cs-CZ', 'Vaše objednávka #{{order_number}} byla doručena', {QuoteSql(deliveredCs)}, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-order-delivered-customer-en', 'tpl-order-delivered-customer', 'en-US', 'Your order #{{order_number}} has been delivered', {QuoteSql(deliveredEn)}, TRUE, 'CZ', 'seed', '{seededAtSql}');
            ");
        }

        private static string QuoteSql(string value) =>
            $"'{value.Replace("'", "''")}'";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse order: drop seed rows then schema change.
            migrationBuilder.Sql(@"
                DELETE FROM email_template_translations
                WHERE id IN (
                    'tpl-tr-order-delivered-customer-cs',
                    'tpl-tr-order-delivered-customer-en'
                );

                DELETE FROM email_templates
                WHERE id IN (
                    'tpl-order-delivered-customer'
                );
            ");

            migrationBuilder.DropColumn(
                name: "delivery_source",
                table: "orders");
        }
    }
}
