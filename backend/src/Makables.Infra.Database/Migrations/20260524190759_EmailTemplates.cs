using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makables.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class EmailTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preferred_language",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "email_template_translations",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    email_template_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    language_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    plain_text_body = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_email_template_translations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_templates",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_template_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    from_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    from_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reply_to_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
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
                    table.PrimaryKey("PK_email_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_email_template_translations_email_template_id_language_code",
                table: "email_template_translations",
                columns: new[] { "email_template_id", "language_code" },
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_email_templates_type",
                table: "email_templates",
                column: "type",
                unique: true,
                filter: "is_active");

            // === T-0028: switch the CZ row to SendGrid (was "resend"). ===
            // ADR 0019 was amended this same ticket; see docs/adr/0019-email-resend.md
            // §"Amendment 2026-05-24". The original Resend decision was reversed
            // because we chose DB-backed translation + SendGrid Dynamic Templates.
            migrationBuilder.Sql(@"
                UPDATE country_configuration
                SET default_email_provider = 'sendgrid'
                WHERE id = 'CZ' AND default_email_provider = 'resend';
            ");

            // === T-0028: seed the three Phase-2 auth email templates. ===
            // SendGrid template ids are placeholders (the 'd-' prefix is
            // SendGrid's dynamic-template convention). They are intentionally
            // unique per template so an admin who pastes the real id later
            // can grep + replace one entry. A future admin UI / config sync
            // (out of scope here) overwrites these with real ids before launch.
            var seededAt = new DateTimeOffset(2026, 5, 24, 0, 0, 0, TimeSpan.Zero);
            var seededAtSql = seededAt.ToString("yyyy-MM-dd HH:mm:sszzz");

            // Plain-text bodies. Single-curly placeholders ({{action_url}} etc.)
            // are substituted by EmailSendService before the message reaches
            // SendGrid. Keep them simple — the HTML is owned by the SendGrid
            // dynamic-template editor.
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

            migrationBuilder.Sql($@"
                INSERT INTO email_templates
                    (id, type, provider_template_id, from_address, from_name, reply_to_address,
                     is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-auth-magic-link',         'AuthMagicLink',         'd-placeholder-auth-magic-link',         NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-auth-email-confirmation', 'AuthEmailConfirmation', 'd-placeholder-auth-email-confirmation', NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-auth-password-reset',     'AuthPasswordReset',     'd-placeholder-auth-password-reset',     NULL, NULL, NULL, TRUE, 'CZ', 'seed', '{seededAtSql}');

                INSERT INTO email_template_translations
                    (id, email_template_id, language_code, subject, plain_text_body, is_active, country_code, created_by, created_at)
                VALUES
                    ('tpl-tr-magic-link-cs',         'tpl-auth-magic-link',         'cs-CZ', 'Přihlášení do Makables',         {QuoteSql(magicLinkCs)},    TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-magic-link-en',         'tpl-auth-magic-link',         'en-US', 'Sign in to Makables',            {QuoteSql(magicLinkEn)},    TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-email-confirmation-cs', 'tpl-auth-email-confirmation', 'cs-CZ', 'Potvrďte svůj e-mail',           {QuoteSql(confirmationCs)}, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-email-confirmation-en', 'tpl-auth-email-confirmation', 'en-US', 'Confirm your email',             {QuoteSql(confirmationEn)}, TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-password-reset-cs',     'tpl-auth-password-reset',     'cs-CZ', 'Reset hesla',                    {QuoteSql(resetCs)},        TRUE, 'CZ', 'seed', '{seededAtSql}'),
                    ('tpl-tr-password-reset-en',     'tpl-auth-password-reset',     'en-US', 'Password reset',                 {QuoteSql(resetEn)},        TRUE, 'CZ', 'seed', '{seededAtSql}');
            ");
        }

        private static string QuoteSql(string value) =>
            $"'{value.Replace("'", "''")}'";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_template_translations");

            migrationBuilder.DropTable(
                name: "email_templates");

            migrationBuilder.DropColumn(
                name: "preferred_language",
                table: "users");

            migrationBuilder.Sql(@"
                UPDATE country_configuration
                SET default_email_provider = 'resend'
                WHERE id = 'CZ' AND default_email_provider = 'sendgrid';
            ");
        }
    }
}
