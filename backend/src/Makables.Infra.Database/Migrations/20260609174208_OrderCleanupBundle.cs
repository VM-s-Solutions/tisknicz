using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <summary>
    /// Order-cleanup bundle migration (T-0079 + T-0083):
    /// <list type="bullet">
    ///   <item><description>T-0079: <c>order_messages</c> table + 2 indexes;
    ///     <c>orders.customer_unread_message_count</c> +
    ///     <c>orders.maker_unread_message_count</c> (INT NOT NULL DEFAULT 0);
    ///     <c>orders.customer_pending_notification_email_at</c> +
    ///     <c>orders.maker_pending_notification_email_at</c> (TIMESTAMPTZ
    ///     NULL); seed three email templates (OrderMessagePostedCustomer +
    ///     OrderMessagePostedMaker) + cs-CZ + en-US translations.</description></item>
    ///   <item><description>T-0083: <c>orders.cancellation_source</c>
    ///     (SMALLINT NULL); seed <c>OrderCancelledAutoExpiryCustomer</c>
    ///     email template + cs-CZ + en-US translations.</description></item>
    /// </list>
    ///
    /// <para>
    /// T-0079 + T-0083 ship in the same bundle PR so the migration applies
    /// atomically. SendGrid <c>provider_template_id</c> values are
    /// placeholders (real ids set via App Service config override
    /// post-deploy). Mirrors the T-0067 / T-0076 seed pattern verbatim.
    /// </para>
    /// </summary>
    public partial class OrderCleanupBundle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // === T-0083 §C: cancellation source SMALLINT NULL. ===
            // Nullable because non-cancelled orders carry null; matches
            // the DeliverySource column shape.
            migrationBuilder.AddColumn<short>(
                name: "cancellation_source",
                table: "orders",
                type: "smallint",
                nullable: true);

            // === T-0079 §C.3: per-counterparty pending-pointer columns. ===
            // Nullable so a fresh order has no pointer; null means
            // "next message post fires the email immediately".
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "customer_pending_notification_email_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            // === T-0079 §C.3: denormalized customer-side unread counter. ===
            migrationBuilder.AddColumn<int>(
                name: "customer_unread_message_count",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // === T-0079 §C.3: per-counterparty pending-pointer columns. ===
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "maker_pending_notification_email_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            // === T-0079 §C.3: denormalized maker-side unread counter. ===
            migrationBuilder.AddColumn<int>(
                name: "maker_unread_message_count",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // === T-0079 §C.3: order_messages child table. ===
            // FK relationships are NOT declared via EF navigations (the
            // Order aggregate stays lightweight per ADR 0013). The
            // application enforces the relationships via the repository
            // surface; the DB only enforces the column shapes + indexes.
            migrationBuilder.CreateTable(
                name: "order_messages",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    order_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    author_role = table.Column<short>(type: "smallint", nullable: false),
                    author_user_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    read_by_counterparty_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_order_messages", x => x.id);
                });

            // Partial index — only unread rows are scanned by the
            // MarkAsRead bulk UPDATE. WHERE filter is Postgres-specific;
            // mirrors the ix_orders_payment_provider_ref convention.
            migrationBuilder.CreateIndex(
                name: "ix_order_messages_order_author_unread",
                table: "order_messages",
                columns: new[] { "order_id", "author_role" },
                filter: "read_by_counterparty_at IS NULL AND is_active");

            // Per-order paged thread read — composite B-tree with DESC on
            // created_at so the ORDER BY needs no sort step.
            migrationBuilder.CreateIndex(
                name: "ix_order_messages_order_created",
                table: "order_messages",
                columns: new[] { "order_id", "created_at" },
                descending: new[] { false, true });

            // === T-0079 / T-0083: seed email templates + cs-CZ + en-US translations. ===
            // Mirrors 20260606155359_SeedOrderEmailTemplates.cs §49-64 verbatim.
            var seededAt = new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);
            var seededAtSql = seededAt.ToString("yyyy-MM-dd HH:mm:sszzz");

            const string messageToCustomerCs =
                "Dobrý den {{contact_name}},\n\n" +
                "máte {{unread_count}} novou zprávu od {{sender_name}} k objednávce {{order_number}}.\n" +
                "Otevřete vlákno: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string messageToCustomerEn =
                "Hi {{contact_name}},\n\n" +
                "you have {{unread_count}} new message(s) from {{sender_name}} on order {{order_number}}.\n" +
                "Open the thread: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string messageToMakerCs =
                "Dobrý den,\n\n" +
                "máte {{unread_count}} novou zprávu od {{sender_name}} k objednávce {{order_number}}.\n" +
                "Otevřete vlákno: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string messageToMakerEn =
                "Hi,\n\n" +
                "you have {{unread_count}} new message(s) from {{sender_name}} on order {{order_number}}.\n" +
                "Open the thread: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string cancelledAutoCs =
                "Dobrý den {{contact_name}},\n\n" +
                "vaše objednávka {{order_number}} byla automaticky zrušena — během 24 hodin od jejího vytvoření jsme neobdrželi platbu.\n" +
                "Pokud máte zájem, můžete si objednávku znovu vytvořit na: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string cancelledAutoEn =
                "Hi {{contact_name}},\n\n" +
                "your order {{order_number}} has been automatically cancelled — we did not receive payment within 24 hours of order creation.\n" +
                "You can re-order at: {{action_url}}\n\n" +
                "Makables — makables.cz";

            migrationBuilder.Sql($@"
                INSERT INTO email_templates
                    (id, type, provider_template_id, from_address, from_name, reply_to_address,
                     is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-order-message-posted-customer',  'OrderMessagePostedCustomer', 'd-placeholder-order-message-posted-customer', NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-order-message-posted-maker',     'OrderMessagePostedMaker',    'd-placeholder-order-message-posted-maker',    NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-order-cancelled-auto-customer',  'OrderCancelledAutoExpiryCustomer', 'd-placeholder-order-cancelled-auto-customer', NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}');

                INSERT INTO email_template_translations
                    (id, email_template_id, language_code, subject, plain_text_body, is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-tr-order-message-posted-customer-cs', 'tpl-order-message-posted-customer', 'cs-CZ', 'Nová zpráva k objednávce #{{order_number}}', {QuoteSql(messageToCustomerCs)}, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-order-message-posted-customer-en', 'tpl-order-message-posted-customer', 'en-US', 'New message on order #{{order_number}}',     {QuoteSql(messageToCustomerEn)}, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-order-message-posted-maker-cs',    'tpl-order-message-posted-maker',    'cs-CZ', 'Nová zpráva k objednávce #{{order_number}}', {QuoteSql(messageToMakerCs)},    TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-order-message-posted-maker-en',    'tpl-order-message-posted-maker',    'en-US', 'New message on order #{{order_number}}',     {QuoteSql(messageToMakerEn)},    TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-order-cancelled-auto-customer-cs', 'tpl-order-cancelled-auto-customer', 'cs-CZ', 'Vaše objednávka #{{order_number}} byla zrušena (platba neproběhla)', {QuoteSql(cancelledAutoCs)}, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-order-cancelled-auto-customer-en', 'tpl-order-cancelled-auto-customer', 'en-US', 'Your order #{{order_number}} was cancelled (payment not received)',  {QuoteSql(cancelledAutoEn)}, TRUE, 'CZ', 'seed', '{seededAtSql}');
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
                    'tpl-tr-order-message-posted-customer-cs',
                    'tpl-tr-order-message-posted-customer-en',
                    'tpl-tr-order-message-posted-maker-cs',
                    'tpl-tr-order-message-posted-maker-en',
                    'tpl-tr-order-cancelled-auto-customer-cs',
                    'tpl-tr-order-cancelled-auto-customer-en'
                );

                DELETE FROM email_templates
                WHERE id IN (
                    'tpl-order-message-posted-customer',
                    'tpl-order-message-posted-maker',
                    'tpl-order-cancelled-auto-customer'
                );
            ");

            migrationBuilder.DropTable(
                name: "order_messages");

            migrationBuilder.DropColumn(
                name: "cancellation_source",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "customer_pending_notification_email_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "customer_unread_message_count",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "maker_pending_notification_email_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "maker_unread_message_count",
                table: "orders");
        }
    }
}
