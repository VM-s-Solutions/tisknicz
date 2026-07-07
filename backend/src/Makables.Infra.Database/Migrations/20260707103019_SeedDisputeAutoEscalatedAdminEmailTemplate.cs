using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <summary>
    /// T-0145 — seed the <c>EmailTemplateType.DisputeAutoEscalatedAdmin</c>
    /// template + cs-CZ + en-US translations, following the
    /// <c>OrderDisputedAdmin</c> admin-notification pattern seeded by
    /// <c>AddDisputeTableAndPreDisputeState</c> (T-0106). No schema change —
    /// the model snapshot is unchanged.
    ///
    /// <para>
    /// <b>DOUBLE-BRACE subjects from birth</b> per the Q-0017 lesson: the
    /// subject literals use <c>{{{{order_number}}}}</c> (quadruple brace)
    /// in the <c>$@"..."</c> SQL so the STORED value is the correct
    /// <c>{{order_number}}</c> the substitution engine expands. Bodies use
    /// non-interpolated <c>const string</c>s, so their <c>{{key}}</c>
    /// tokens are already correct.
    /// </para>
    /// </summary>
    public partial class SeedDisputeAutoEscalatedAdminEmailTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var seededAt = new System.DateTimeOffset(2026, 7, 7, 0, 0, 0, System.TimeSpan.Zero);
            var seededAtSql = seededAt.ToString("yyyy-MM-dd HH:mm:sszzz");

            // Admin digest — same shape as tpl-order-disputed-admin, framed
            // around the missed SLA rather than the open event. Notification
            // only: the copy makes clear the dispute is still open, awaiting
            // an admin decision.
            const string autoEscalatedAdminCs =
                "Dobrý den,\n\n" +
                "výrobce nereagoval do 7 dnů na reklamaci k objednávce {{order_number}} a případ byl automaticky eskalován.\n\n" +
                "Kategorie: {{category}}\n" +
                "Popis: {{description}}\n\n" +
                "Reklamace zůstává otevřená a čeká na vaše rozhodnutí: {{action_url}}\n\n" +
                "Makables — makables.cz";
            const string autoEscalatedAdminEn =
                "Hi,\n\n" +
                "the maker did not respond within 7 days to the dispute on order {{order_number}} — it has been auto-escalated.\n\n" +
                "Category: {{category}}\n" +
                "Description: {{description}}\n\n" +
                "The dispute is still open, awaiting your decision: {{action_url}}\n\n" +
                "Makables — makables.cz";

            migrationBuilder.Sql($@"
                INSERT INTO email_templates
                    (id, type, provider_template_id, from_address, from_name, reply_to_address,
                     is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-dispute-auto-escalated-admin', 'DisputeAutoEscalatedAdmin', 'd-placeholder-dispute-auto-escalated-admin', NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}');

                INSERT INTO email_template_translations
                    (id, email_template_id, language_code, subject, plain_text_body, is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-tr-dispute-auto-escalated-admin-cs', 'tpl-dispute-auto-escalated-admin', 'cs-CZ', 'Reklamace k objednávce #{{{{order_number}}}} eskalována — výrobce nereagoval', {QuoteSql(autoEscalatedAdminCs)}, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-dispute-auto-escalated-admin-en', 'tpl-dispute-auto-escalated-admin', 'en-US', 'Dispute on order #{{{{order_number}}}} auto-escalated — maker did not respond', {QuoteSql(autoEscalatedAdminEn)}, TRUE, 'CZ', 'seed', '{seededAtSql}');
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
                    'tpl-tr-dispute-auto-escalated-admin-cs',
                    'tpl-tr-dispute-auto-escalated-admin-en'
                );

                DELETE FROM email_templates
                WHERE id = 'tpl-dispute-auto-escalated-admin';
            ");
        }
    }
}
