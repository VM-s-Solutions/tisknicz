using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <summary>
    /// T-0102b — seed the <c>EmailTemplateType.PayoutFeeInvoiceMaker</c>
    /// template + cs-CZ (tykání — maker audience) + en-US translations. The
    /// Fee invoice PDF rides as an attachment looked up at send time
    /// (T-0069 pattern); this template carries the body + subject only.
    ///
    /// <para>
    /// <b>DOUBLE-BRACE subjects from birth</b> per the Q-0017 lesson: the
    /// subject literals use <c>{{{{batch_number}}}}</c> (quadruple brace) in
    /// the <c>$@"..."</c> SQL so the STORED value is the correct
    /// <c>{{batch_number}}</c> the substitution engine expands. Bodies use
    /// non-interpolated <c>const string</c>s, so their <c>{{key}}</c> tokens
    /// are already correct. No schema change → the model snapshot is
    /// unchanged.
    /// </para>
    /// </summary>
    public partial class SeedPayoutFeeInvoiceEmailTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var seededAt = new System.DateTimeOffset(2026, 6, 13, 0, 0, 0, System.TimeSpan.Zero);
            var seededAtSql = seededAt.ToString("yyyy-MM-dd HH:mm:sszzz");

            // Tykání (maker audience). {{fee_amount}} is the formatted total
            // pre-computed by EmailSendService; {{action_url}} links the
            // maker payout dashboard.
            const string feeCs =
                "Ahoj,\n\n" +
                "k výplatní dávce {{batch_number}} jsme ti vystavili fakturu za provizi " +
                "{{invoice_number}} ve výši {{fee_amount}} {{currency}}.\n" +
                "Fakturu najdeš v příloze tohoto e-mailu i ve svém přehledu výplat: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string feeEn =
                "Hi,\n\n" +
                "for payout batch {{batch_number}} we have issued your platform-fee invoice " +
                "{{invoice_number}} for {{fee_amount}} {{currency}}.\n" +
                "The invoice is attached to this email and available in your payouts overview: {{action_url}}\n\n" +
                "Makables — makables.cz";

            migrationBuilder.Sql($@"
                INSERT INTO email_templates
                    (id, type, provider_template_id, from_address, from_name, reply_to_address,
                     is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-payout-fee-invoice-maker', 'PayoutFeeInvoiceMaker', 'd-placeholder-payout-fee-invoice-maker', NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}');

                INSERT INTO email_template_translations
                    (id, email_template_id, language_code, subject, plain_text_body, is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-tr-payout-fee-invoice-maker-cs', 'tpl-payout-fee-invoice-maker', 'cs-CZ', 'Faktura provize za výplatní dávku {{{{batch_number}}}}', {QuoteSql(feeCs)}, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-payout-fee-invoice-maker-en', 'tpl-payout-fee-invoice-maker', 'en-US', 'Platform-fee invoice for payout batch {{{{batch_number}}}}', {QuoteSql(feeEn)}, TRUE, 'CZ', 'seed', '{seededAtSql}');
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
                    'tpl-tr-payout-fee-invoice-maker-cs',
                    'tpl-tr-payout-fee-invoice-maker-en'
                );

                DELETE FROM email_templates
                WHERE id = 'tpl-payout-fee-invoice-maker';
            ");
        }
    }
}
