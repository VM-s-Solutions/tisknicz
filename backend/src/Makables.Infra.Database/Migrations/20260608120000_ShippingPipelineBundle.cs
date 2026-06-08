using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <summary>
    /// Shipping-pipeline bundle migration: adds the Order
    /// <c>shipping_carrier_tracking_url</c> column (T-0070); sets CZ
    /// <c>default_shipping_carrier = 'packeta'</c> (T-0070); seeds the
    /// <c>OrderAcceptedCustomer</c> (T-0071) + <c>OrderShippedCustomer</c>
    /// (T-0072) email templates with cs-CZ + en-US translations.
    ///
    /// <para>
    /// Combined into a single migration because the four changes ship in
    /// the same PR (shipping-pipeline bundle) and must apply atomically.
    /// </para>
    /// </summary>
    public partial class ShippingPipelineBundle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // === T-0070 §A.1: Order.ShippingCarrierTrackingUrl column. ===
            migrationBuilder.AddColumn<string>(
                name: "shipping_carrier_tracking_url",
                table: "orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // === T-0070 §A.7 + §B: CZ default shipping carrier = 'packeta'. ===
            // The provider seed for CZ may already have a value (admin edits
            // possible); we deliberately overwrite to anchor the launch
            // posture. country_configuration.id == 'CZ' per Country.Create.
            migrationBuilder.Sql(@"
                UPDATE country_configuration
                   SET default_shipping_carrier = 'packeta'
                 WHERE id = 'CZ';
            ");

            // === T-0071: seed OrderAcceptedCustomer template + 2 translations. ===
            // Mirrors 20260606155359_SeedOrderEmailTemplates.cs §49-64 verbatim.
            var seededAt = new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero);
            var seededAtSql = seededAt.ToString("yyyy-MM-dd HH:mm:sszzz");

            const string acceptedCs =
                "Dobrý den {{contact_name}},\n\n" +
                "váš výrobce přijal objednávku {{order_number}} a začíná na ní pracovat.\n" +
                "Aktuální stav najdete na: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string acceptedEn =
                "Hi {{contact_name}},\n\n" +
                "your maker has accepted order {{order_number}} and is starting work.\n" +
                "Track its status at: {{action_url}}\n\n" +
                "Makables — makables.cz";

            // === T-0072: seed OrderShippedCustomer template + 2 translations. ===
            // Conditional tracking_url block renders only when the
            // substitution is non-empty (T-0073 personal-pickup sets null).
            const string shippedCs =
                "Dobrý den {{contact_name}},\n\n" +
                "objednávka {{order_number}} byla odeslána a je na cestě k vám.\n" +
                "{{tracking_url}}\n" +
                "Detail najdete na: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string shippedEn =
                "Hi {{contact_name}},\n\n" +
                "your order {{order_number}} has shipped and is on its way to you.\n" +
                "{{tracking_url}}\n" +
                "View details at: {{action_url}}\n\n" +
                "Makables — makables.cz";

            migrationBuilder.Sql($@"
                INSERT INTO email_templates
                    (id, type, provider_template_id, from_address, from_name, reply_to_address,
                     is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-order-accepted-customer', 'OrderAcceptedCustomer', 'd-placeholder-order-accepted-customer', NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-order-shipped-customer',  'OrderShippedCustomer',  'd-placeholder-order-shipped-customer',  NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}');

                INSERT INTO email_template_translations
                    (id, email_template_id, language_code, subject, plain_text_body, is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-tr-order-accepted-customer-cs', 'tpl-order-accepted-customer', 'cs-CZ', 'Vaše objednávka #{{order_number}} byla přijata', {QuoteSql(acceptedCs)}, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-order-accepted-customer-en', 'tpl-order-accepted-customer', 'en-US', 'Your order #{{order_number}} has been accepted', {QuoteSql(acceptedEn)}, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-order-shipped-customer-cs',  'tpl-order-shipped-customer',  'cs-CZ', 'Vaše objednávka #{{order_number}} byla odeslána', {QuoteSql(shippedCs)},  TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-order-shipped-customer-en',  'tpl-order-shipped-customer',  'en-US', 'Your order #{{order_number}} has shipped',         {QuoteSql(shippedEn)},  TRUE, 'CZ', 'seed', '{seededAtSql}');
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
                    'tpl-tr-order-accepted-customer-cs',
                    'tpl-tr-order-accepted-customer-en',
                    'tpl-tr-order-shipped-customer-cs',
                    'tpl-tr-order-shipped-customer-en'
                );

                DELETE FROM email_templates
                WHERE id IN (
                    'tpl-order-accepted-customer',
                    'tpl-order-shipped-customer'
                );

                -- Restore the prior CZ default carrier to NULL/empty —
                -- best-effort reversal; admin can re-seed manually.
                UPDATE country_configuration
                   SET default_shipping_carrier = ''
                 WHERE id = 'CZ' AND default_shipping_carrier = 'packeta';
            ");

            migrationBuilder.DropColumn(
                name: "shipping_carrier_tracking_url",
                table: "orders");
        }
    }
}
