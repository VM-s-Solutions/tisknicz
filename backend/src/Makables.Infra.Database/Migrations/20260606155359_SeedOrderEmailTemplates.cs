using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class SeedOrderEmailTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // === T-0067: seed the two Phase-4 order email templates. ===
            // Mirrors the T-0028 seed pattern at
            // Migrations/20260524190759_EmailTemplates.cs §142-159.
            // SendGrid template ids are placeholders (the 'd-' prefix is
            // SendGrid's dynamic-template convention). Real ids land in a
            // deploy-time Azure App Service configuration override.
            var seededAt = new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero);
            var seededAtSql = seededAt.ToString("yyyy-MM-dd HH:mm:sszzz");

            // Minimal plain-text bodies — the rich HTML rendering lives in
            // the SendGrid Dynamic Template. The body is a fallback for
            // recipients whose mail client can't render HTML; placeholders
            // are substituted by EmailSendService before the message
            // reaches SendGrid.
            const string customerCs =
                "Dobrý den {{contact_name}},\n\n" +
                "děkujeme za objednávku {{order_number}} v hodnotě {{total_amount}} {{currency}}.\n" +
                "Detail objednávky najdete na: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string customerEn =
                "Hi {{contact_name}},\n\n" +
                "thanks for your order {{order_number}} for {{total_amount}} {{currency}}.\n" +
                "View order details at: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string makerCs =
                "Dobrý den,\n\n" +
                "máte novou objednávku {{order_number}} v hodnotě {{total_amount}} {{currency}}.\n" +
                "Detail najdete v dashboardu: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string makerEn =
                "Hi,\n\n" +
                "you have a new order {{order_number}} for {{total_amount}} {{currency}}.\n" +
                "View it in your dashboard at: {{action_url}}\n\n" +
                "Makables — makables.cz";

            migrationBuilder.Sql($@"
                INSERT INTO email_templates
                    (id, type, provider_template_id, from_address, from_name, reply_to_address,
                     is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-order-paid-customer',  'OrderPaidCustomer', 'd-placeholder-order-paid-customer', NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-order-placed-maker',   'OrderPlacedMaker',  'd-placeholder-order-placed-maker',  NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}');

                INSERT INTO email_template_translations
                    (id, email_template_id, language_code, subject, plain_text_body, is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-tr-order-paid-customer-cs', 'tpl-order-paid-customer', 'cs-CZ', 'Děkujeme za objednávku #{{order_number}}', {QuoteSql(customerCs)}, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-order-paid-customer-en', 'tpl-order-paid-customer', 'en-US', 'Thanks for your order #{{order_number}}',  {QuoteSql(customerEn)}, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-order-placed-maker-cs',  'tpl-order-placed-maker',  'cs-CZ', 'Nová objednávka #{{order_number}}',         {QuoteSql(makerCs)},    TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-order-placed-maker-en',  'tpl-order-placed-maker',  'en-US', 'New order #{{order_number}}',               {QuoteSql(makerEn)},    TRUE, 'CZ', 'seed', '{seededAtSql}');
            ");
        }

        private static string QuoteSql(string value) =>
            $"'{value.Replace("'", "''")}'";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM email_template_translations
                WHERE id IN (
                    'tpl-tr-order-paid-customer-cs',
                    'tpl-tr-order-paid-customer-en',
                    'tpl-tr-order-placed-maker-cs',
                    'tpl-tr-order-placed-maker-en'
                );

                DELETE FROM email_templates
                WHERE id IN (
                    'tpl-order-paid-customer',
                    'tpl-order-placed-maker'
                );
            ");
        }
    }
}
