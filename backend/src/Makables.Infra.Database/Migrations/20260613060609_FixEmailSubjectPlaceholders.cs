using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <summary>
    /// Q-0017 data-fix. 16 <c>email_template_translations.subject</c> rows
    /// were seeded with single-brace placeholders (<c>#{order_number}</c>)
    /// because the seed SQL was built inside C# <c>$@"..."</c> interpolated
    /// strings where the intended <c>{{order_number}}</c> literal collapses
    /// to <c>{order_number}</c>. The double-brace substitution engine
    /// (<c>EmailSendService.SubstitutePlainTextPlaceholders</c>) only expands
    /// <c>{{key}}</c> tokens, so every affected subject currently renders the
    /// raw single-brace placeholder to recipients.
    ///
    /// <para>
    /// Affected rows (from the 4 prior seed migrations): SeedOrderEmailTemplates
    /// ×4, ShippingPipelineBundle ×4, DeliveryCloseBundle ×2, OrderCleanupBundle
    /// ×6 — all carrying <c>{order_number}</c> in the subject.
    /// </para>
    ///
    /// <para>
    /// <b>Idempotent.</b> The UPDATE targets only rows whose subject still
    /// contains a single-brace <c>{order_number}</c> AND does NOT already
    /// contain the double-brace <c>{{order_number}}</c> — so the already-correct
    /// T-0105/T-0106 seed rows (and a re-run of this migration) are untouched.
    /// Bodies were built via non-interpolated constants and are unaffected.
    /// No schema change → the model snapshot is unchanged.
    /// </para>
    /// </summary>
    public partial class FixEmailSubjectPlaceholders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE email_template_translations
                SET subject = REPLACE(subject, '{order_number}', '{{order_number}}')
                WHERE subject LIKE '%{order_number}%'
                  AND subject NOT LIKE '%{{order_number}}%';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the original (broken) single-brace form. Only touch rows
            // that currently carry the double-brace token so a Down after a
            // partial re-seed stays idempotent.
            migrationBuilder.Sql(@"
                UPDATE email_template_translations
                SET subject = REPLACE(subject, '{{order_number}}', '{order_number}')
                WHERE subject LIKE '%{{order_number}}%';
            ");
        }
    }
}
