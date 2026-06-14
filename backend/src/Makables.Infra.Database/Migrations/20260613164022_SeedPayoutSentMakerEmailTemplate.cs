using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <summary>
    /// T-0103 — seed the <c>EmailTemplateType.PayoutSentMaker</c> template +
    /// cs-CZ (tykání — maker audience) + en-US translations. The payout-sent
    /// email is a plain summary (no PDF attachment, unlike the fee-invoice
    /// email): batch number, total paid to this maker, order count, and a deep
    /// link to the maker fee-invoice page. Runs after the bank-reference
    /// migration; no schema change → the model snapshot is unchanged.
    ///
    /// <para>
    /// <b>DOUBLE-BRACE subjects from birth</b> per the Q-0017 lesson: the
    /// subject literals use <c>{{{{batch_number}}}}</c> (quadruple brace) in
    /// the <c>$@"..."</c> SQL so the STORED value is the correct
    /// <c>{{batch_number}}</c> the substitution engine expands. Bodies use
    /// non-interpolated <c>const string</c>s, so their <c>{{key}}</c> tokens
    /// are already correct.
    /// </para>
    /// </summary>
    public partial class SeedPayoutSentMakerEmailTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var seededAt = new System.DateTimeOffset(2026, 6, 13, 0, 0, 0, System.TimeSpan.Zero);
            var seededAtSql = seededAt.ToString("yyyy-MM-dd HH:mm:sszzz");

            // Tykání (maker audience). {{total_paid}} is the formatted total
            // pre-computed by EmailSendService; {{fee_invoice_url}} links the
            // maker payout / fee-invoice page.
            const string sentCs =
                "Ahoj,\n\n" +
                "vyplatili jsme ti výplatu za dávku {{batch_number}}: " +
                "{{total_paid}} {{currency}} za {{order_count}} objednávek.\n" +
                "Fakturu za provizi a rozpis objednávek najdeš ve svém přehledu výplat: {{fee_invoice_url}}\n\n" +
                "Makables — makables.cz";
            const string sentEn =
                "Hi,\n\n" +
                "we have sent your payout for batch {{batch_number}}: " +
                "{{total_paid}} {{currency}} for {{order_count}} orders.\n" +
                "Your platform-fee invoice and the per-order breakdown are in your payouts overview: {{fee_invoice_url}}\n\n" +
                "Makables — makables.cz";

            migrationBuilder.Sql($@"
                INSERT INTO email_templates
                    (id, type, provider_template_id, from_address, from_name, reply_to_address,
                     is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-payout-sent-maker', 'PayoutSentMaker', 'd-placeholder-payout-sent-maker', NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}');

                INSERT INTO email_template_translations
                    (id, email_template_id, language_code, subject, plain_text_body, is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-tr-payout-sent-maker-cs', 'tpl-payout-sent-maker', 'cs-CZ', 'Výplata za dávku {{{{batch_number}}}} odeslána', {QuoteSql(sentCs)}, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-payout-sent-maker-en', 'tpl-payout-sent-maker', 'en-US', 'Payout for batch {{{{batch_number}}}} has been sent', {QuoteSql(sentEn)}, TRUE, 'CZ', 'seed', '{seededAtSql}');
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
                    'tpl-tr-payout-sent-maker-cs',
                    'tpl-tr-payout-sent-maker-en'
                );

                DELETE FROM email_templates
                WHERE id = 'tpl-payout-sent-maker';
            ");
        }
    }
}
