using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <summary>
    /// Re-writes the copy of the three no-reply auth emails (magic link,
    /// e-mail confirmation, password reset) for the HTML layout that now
    /// wraps them, and drops the literal " UTC" that followed
    /// <c>{{expires_at}}</c>.
    ///
    /// <para>
    /// Two things changed underneath this copy. First, <c>EmailSendService</c>
    /// now renders <c>{{expires_at}}</c> through <c>EmailFormatting</c> —
    /// the recipient's own wall clock in the country's own date pattern
    /// ("24. 8. 2026, 20:30"), so a trailing "UTC" would be an outright
    /// lie. Second, <c>EmailHtmlLayout</c> derives the HTML part from this
    /// very body: blocks separated by blank lines become paragraphs, a
    /// block that is nothing but the action URL becomes the button, and
    /// the trailing "Makables — makables.cz" sign-off is dropped because
    /// the shell's footer already carries it.
    /// </para>
    ///
    /// <para>
    /// The copy itself is re-cut for that shell. The bare "Dobrý den,"
    /// opener is gone: it was the first prose block, which means it was
    /// also the inbox preview line, and "Dobrý den," tells a recipient
    /// nothing about why the mail arrived. Each body now opens with the
    /// reason. Vykání throughout — these reach customers and makers alike,
    /// and the formal register is the safe one.
    /// </para>
    ///
    /// <para>
    /// Data-only: no schema change, so the model snapshot is untouched.
    /// Both directions are idempotent UPDATEs keyed by translation id.
    /// </para>
    /// </summary>
    public partial class AuthEmailCopyCzechLocale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            const string magicLinkCs =
                "Přihlaste se do Makables přes odkaz níže — heslo nebudete potřebovat.\n\n" +
                "{{action_url}}\n\n" +
                "Odkaz je platný do {{expires_at}} a lze jej použít jen jednou.\n\n" +
                "Pokud jste o přihlášení nežádali, tento e-mail ignorujte.\n\n" +
                "Makables — makables.cz";
            const string magicLinkEn =
                "Sign in to Makables using the link below — no password needed.\n\n" +
                "{{action_url}}\n\n" +
                "The link is valid until {{expires_at}} and can be used once.\n\n" +
                "If you didn't request a sign-in, ignore this email.\n\n" +
                "Makables — makables.cz";

            const string confirmationCs =
                "Vítejte v Makables. Zbývá poslední krok — potvrďte prosím, že tato e-mailová adresa patří vám.\n\n" +
                "{{action_url}}\n\n" +
                "Odkaz je platný do {{expires_at}}.\n\n" +
                "Pokud jste si u nás účet nezakládali, tento e-mail ignorujte — bez potvrzení se s adresou nic nestane.\n\n" +
                "Makables — makables.cz";
            const string confirmationEn =
                "Welcome to Makables. One last step — please confirm that this email address is yours.\n\n" +
                "{{action_url}}\n\n" +
                "The link is valid until {{expires_at}}.\n\n" +
                "If you didn't create an account with us, ignore this email — nothing happens to the address without confirmation.\n\n" +
                "Makables — makables.cz";

            const string resetCs =
                "Dostali jsme žádost o nastavení nového hesla k vašemu účtu Makables. Pokračujte přes odkaz níže.\n\n" +
                "{{action_url}}\n\n" +
                "Odkaz je platný do {{expires_at}}.\n\n" +
                "Pokud jste o obnovení hesla nežádali, e-mail ignorujte — vaše stávající heslo zůstává v platnosti.\n\n" +
                "Makables — makables.cz";
            const string resetEn =
                "We received a request to set a new password for your Makables account. Continue using the link below.\n\n" +
                "{{action_url}}\n\n" +
                "The link is valid until {{expires_at}}.\n\n" +
                "If you didn't request a password reset, ignore this email — your current password stays valid.\n\n" +
                "Makables — makables.cz";

            // "Reset hesla" was a half-translation; the flow is called
            // "obnovení hesla" everywhere else in the Czech UI.
            UpdateTranslation(migrationBuilder, "tpl-tr-magic-link-cs", "Přihlášení do Makables", magicLinkCs);
            UpdateTranslation(migrationBuilder, "tpl-tr-magic-link-en", "Sign in to Makables", magicLinkEn);
            UpdateTranslation(migrationBuilder, "tpl-tr-email-confirmation-cs", "Potvrďte svůj e-mail", confirmationCs);
            UpdateTranslation(migrationBuilder, "tpl-tr-email-confirmation-en", "Confirm your email", confirmationEn);
            UpdateTranslation(migrationBuilder, "tpl-tr-password-reset-cs", "Obnovení hesla", resetCs);
            UpdateTranslation(migrationBuilder, "tpl-tr-password-reset-en", "Password reset", resetEn);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Verbatim restore of the T-0028 seed (20260524190759_EmailTemplates).
            const string magicLinkCs =
                "Dobrý den,\n\n" +
                "klikněte na odkaz níže pro přihlášení do Makables:\n\n" +
                "{{action_url}}\n\n" +
                "Odkaz vyprší {{expires_at}} UTC. Pokud jste o přihlášení nežádali, tento e-mail ignorujte.\n\n" +
                "Makables — makables.cz";
            const string magicLinkEn =
                "Hi,\n\n" +
                "click the link below to sign in to Makables:\n\n" +
                "{{action_url}}\n\n" +
                "The link expires at {{expires_at}} UTC. If you didn't request a sign-in, ignore this email.\n\n" +
                "Makables — makables.cz";
            const string confirmationCs =
                "Dobrý den,\n\n" +
                "potvrďte prosím svou e-mailovou adresu kliknutím na odkaz:\n\n" +
                "{{action_url}}\n\n" +
                "Odkaz je platný do {{expires_at}} UTC.\n\n" +
                "Makables — makables.cz";
            const string confirmationEn =
                "Hi,\n\n" +
                "please confirm your email address by clicking the link:\n\n" +
                "{{action_url}}\n\n" +
                "The link is valid until {{expires_at}} UTC.\n\n" +
                "Makables — makables.cz";
            const string resetCs =
                "Dobrý den,\n\n" +
                "obdrželi jsme žádost o reset hesla. Pokračujte kliknutím na odkaz:\n\n" +
                "{{action_url}}\n\n" +
                "Odkaz vyprší {{expires_at}} UTC. Pokud jste o reset nežádali, tento e-mail ignorujte — vaše heslo zůstává beze změny.\n\n" +
                "Makables — makables.cz";
            const string resetEn =
                "Hi,\n\n" +
                "we received a password-reset request. To continue, click the link:\n\n" +
                "{{action_url}}\n\n" +
                "The link expires at {{expires_at}} UTC. If you didn't request a reset, ignore this email — your password is unchanged.\n\n" +
                "Makables — makables.cz";

            UpdateTranslation(migrationBuilder, "tpl-tr-magic-link-cs", "Přihlášení do Makables", magicLinkCs);
            UpdateTranslation(migrationBuilder, "tpl-tr-magic-link-en", "Sign in to Makables", magicLinkEn);
            UpdateTranslation(migrationBuilder, "tpl-tr-email-confirmation-cs", "Potvrďte svůj e-mail", confirmationCs);
            UpdateTranslation(migrationBuilder, "tpl-tr-email-confirmation-en", "Confirm your email", confirmationEn);
            UpdateTranslation(migrationBuilder, "tpl-tr-password-reset-cs", "Reset hesla", resetCs);
            UpdateTranslation(migrationBuilder, "tpl-tr-password-reset-en", "Password reset", resetEn);
        }

        private static void UpdateTranslation(
            MigrationBuilder migrationBuilder, string id, string subject, string body) =>
            migrationBuilder.Sql($@"
                UPDATE email_template_translations
                SET subject = {QuoteSql(subject)}, plain_text_body = {QuoteSql(body)}
                WHERE id = {QuoteSql(id)};
            ");

        private static string QuoteSql(string value) =>
            $"'{value.Replace("'", "''")}'";
    }
}
