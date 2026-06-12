using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <summary>
    /// T-0105 — admin refund support:
    /// <list type="bullet">
    ///   <item><description><c>orders.refunded_amount_minor</c>
    ///     (BIGINT NOT NULL DEFAULT 0) — cumulative partial-refund
    ///     accumulator per user decision Q1; existing rows backfill 0 via
    ///     the DEFAULT. Money convention: <c>_minor</c> suffix + the
    ///     row's existing <c>currency CHAR(3)</c> column. <c>refunded_at</c>
    ///     already exists from T-0060.</description></item>
    ///   <item><description>Seed <c>EmailTemplateType.OrderRefundedCustomer</c>
    ///     template + cs-CZ + en-US translations (T-0067 seed-in-migration
    ///     precedent; SendGrid <c>provider_template_id</c> is a
    ///     placeholder overridden via App Service config post-deploy).</description></item>
    /// </list>
    /// </summary>
    public partial class AddOrderRefundedAmountAndRefundEmailTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "refunded_amount_minor",
                table: "orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // === T-0105: seed the refund customer email template + translations. ===
            // Mirrors 20260609174208_OrderCleanupBundle.cs §143-197 verbatim.
            var seededAt = new System.DateTimeOffset(2026, 6, 12, 0, 0, 0, System.TimeSpan.Zero);
            var seededAtSql = seededAt.ToString("yyyy-MM-dd HH:mm:sszzz");

            // {{refund_kind}} renders "plná" / "částečná" (full / partial)
            // and {{refunded_amount}} the formatted amount — both are
            // pre-computed by EmailSendService; the plain-text body has no
            // conditional syntax.
            const string refundedCs =
                "Dobrý den {{contact_name}},\n\n" +
                "k vaší objednávce {{order_number}} byla provedena {{refund_kind}} refundace " +
                "ve výši {{refunded_amount}} {{currency}}.\n" +
                "Peníze se na váš účet vrátí stejnou cestou, jakou proběhla platba (obvykle do několika pracovních dnů).\n" +
                "Detail objednávky: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string refundedEn =
                "Hi {{contact_name}},\n\n" +
                "a refund of {{refunded_amount}} {{currency}} has been issued for your order {{order_number}}.\n" +
                "The money will return to your account the same way the payment was made (usually within a few business days).\n" +
                "Order detail: {{action_url}}\n\n" +
                "Makables — makables.cz";

            migrationBuilder.Sql($@"
                INSERT INTO email_templates
                    (id, type, provider_template_id, from_address, from_name, reply_to_address,
                     is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-order-refunded-customer', 'OrderRefundedCustomer', 'd-placeholder-order-refunded-customer', NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}');

                INSERT INTO email_template_translations
                    (id, email_template_id, language_code, subject, plain_text_body, is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-tr-order-refunded-customer-cs', 'tpl-order-refunded-customer', 'cs-CZ', 'Vrácení peněz k objednávce #{{{{order_number}}}}', {QuoteSql(refundedCs)}, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-order-refunded-customer-en', 'tpl-order-refunded-customer', 'en-US', 'Refund for order #{{{{order_number}}}}', {QuoteSql(refundedEn)}, TRUE, 'CZ', 'seed', '{seededAtSql}');
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
                    'tpl-tr-order-refunded-customer-cs',
                    'tpl-tr-order-refunded-customer-en'
                );

                DELETE FROM email_templates
                WHERE id = 'tpl-order-refunded-customer';
            ");

            migrationBuilder.DropColumn(
                name: "refunded_amount_minor",
                table: "orders");
        }
    }
}
