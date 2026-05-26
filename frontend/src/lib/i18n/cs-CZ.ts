/**
 * Czech message catalog for Makables. The MVP ships Czech-only per
 * CLAUDE.md; this file is the single source of truth so future locale
 * additions (T-0130 placeholder) only need to add a sibling catalog and
 * a locale selector. Until then, components import keys from here via
 * the {@link t} helper.
 *
 * Keys are dotted, grouped by domain (`auth.*`, `catalog.*`, `order.*`).
 * `{placeholder}` segments are replaced by {@link t} with positional
 * arguments.
 */

export const messages = {
  // Common
  'common.app_name': 'Makables',
  'common.tagline': 'Where Ideas Take Shape.',
  'common.loading': 'Načítání…',
  'common.retry': 'Zkusit znovu',
  'common.back': 'Zpět',
  'common.cancel': 'Zrušit',
  'common.confirm': 'Potvrdit',
  'common.save': 'Uložit',
  'common.delete': 'Smazat',
  'common.edit': 'Upravit',

  // Errors (mirror ApiError.type)
  'error.validation': 'Některé údaje neprošly validací.',
  'error.unauthorized': 'Pro pokračování se prosím přihlaste.',
  'error.forbidden': 'K této akci nemáte oprávnění.',
  'error.not_found': 'Požadovaný záznam nebyl nalezen.',
  'error.conflict': 'Tato akce není v aktuálním stavu povolena.',
  'error.transient': 'Server je momentálně nedostupný. Zkuste to prosím znovu.',
  'error.permanent': 'Akci se nepodařilo dokončit.',
  'error.configuration': 'Konfigurace platformy neumožňuje tuto akci. Kontaktujte podporu.',
  'error.unknown': 'Něco se pokazilo.',

  // Auth — login
  'auth.login.title': 'Přihlášení',
  'auth.login.email': 'E-mail',
  'auth.login.password': 'Heslo',
  'auth.login.submit': 'Přihlásit se',
  'auth.login.submitting': 'Přihlašuji…',
  'auth.login.forgot_password': 'Zapomněli jste heslo?',
  'auth.login.magic_link': 'Přihlásit se odkazem v e-mailu',
  'auth.login.no_account': 'Ještě nemáte účet?',
  'auth.login.register_link': 'Vytvořit účet',
  'auth.login.invalid_credentials': 'Nesprávný e-mail nebo heslo.',
  'auth.login.account_locked': 'Účet je dočasně uzamčen kvůli opakovaným neúspěšným pokusům. Zkuste to za chvíli.',
  'auth.login.email_not_confirmed': 'E-mail dosud nebyl potvrzen. Zkontrolujte prosím schránku.',

  // Auth — register (shared)
  'auth.register.title': 'Vytvořit zákaznický účet',
  'auth.register.full_name': 'Jméno a příjmení',
  'auth.register.email': 'E-mail',
  'auth.register.password': 'Heslo',
  'auth.register.password_hint': 'Alespoň 10 znaků.',
  'auth.register.submit': 'Vytvořit účet',
  'auth.register.submitting': 'Vytvářím…',
  'auth.register.already_have_account': 'Máte už účet?',
  'auth.register.login_link': 'Přihlásit se',
  'auth.register.success_title': 'Účet vytvořen',
  'auth.register.success_body': 'Poslali jsme vám potvrzovací e-mail. Klikněte na odkaz pro aktivaci účtu.',
  'auth.register.email_already_exists': 'Tento e-mail je již zaregistrován.',
  'auth.register.maker_link': 'Jste výrobce? Zaregistrujte se zde.',

  // Auth — register maker
  'auth.register_maker.title': 'Registrace výrobce',
  'auth.register_maker.intro': 'Vyplňte IČO. Ostatní údaje doplníme z veřejného rejstříku ARES.',
  'auth.register_maker.ico': 'IČO',
  'auth.register_maker.ico_hint': '8 číslic, např. 27074358.',
  'auth.register_maker.submit': 'Pokračovat',
  'auth.register_maker.ico_invalid': 'IČO je neplatné nebo neexistuje v rejstříku ARES.',
  'auth.register_maker.ico_already_registered': 'Tento výrobce už má u nás účet.',
  'auth.register_maker.company_dissolved': 'Tato firma je v ARES vedena jako zaniklá. Nelze ji registrovat.',
  'auth.register_maker.snapshot_stale_notice': 'Údaje z ARES jsou starší. Administrátor je obnoví při ověření.',

  // Auth — verify
  'auth.verify.title': 'Potvrzení e-mailu',
  'auth.verify.confirming': 'Ověřuji e-mail…',
  'auth.verify.success_title': 'E-mail potvrzen',
  'auth.verify.success_body': 'Děkujeme, váš účet je nyní aktivní. Můžete se přihlásit.',
  'auth.verify.failed_title': 'Potvrzení se nezdařilo',
  'auth.verify.failed_body': 'Odkaz je neplatný nebo už vypršel.',
  'auth.verify.missing_token': 'V odkazu chybí potvrzovací kód.',
  'auth.verify.resend': 'Poslat odkaz znovu',
  'auth.verify.banner': 'Váš e-mail dosud nebyl potvrzen.',
  'auth.verify.banner_action': 'Poslat znovu',
  'auth.verify.banner_sent': 'Odesláno',

  // Auth — password reset
  'auth.reset.request_title': 'Obnova hesla',
  'auth.reset.request_intro': 'Zadejte svůj e-mail; pošleme vám odkaz pro nastavení nového hesla.',
  'auth.reset.request_submit': 'Odeslat odkaz',
  'auth.reset.request_done_title': 'Zkontrolujte e-mail',
  'auth.reset.request_done_body': 'Pokud k zadanému e-mailu existuje účet, právě jsme tam poslali odkaz pro obnovu hesla.',
  'auth.reset.confirm_title': 'Nové heslo',
  'auth.reset.confirm_intro': 'Zadejte nové heslo; musí mít alespoň 10 znaků.',
  'auth.reset.confirm_submit': 'Nastavit heslo',
  'auth.reset.confirm_done_title': 'Heslo nastaveno',
  'auth.reset.confirm_done_body': 'Nyní se přihlaste novým heslem.',
  'auth.reset.confirm_missing_token': 'V odkazu chybí kód pro obnovu hesla.',
  'auth.reset.confirm_failed': 'Odkaz je neplatný nebo už vypršel.',

  // Auth — magic link
  'auth.magic.title': 'Přihlášení odkazem',
  'auth.magic.consuming': 'Přihlašuji…',
  'auth.magic.failed_title': 'Přihlášení se nezdařilo',
  'auth.magic.failed_body': 'Odkaz je neplatný nebo už vypršel.',
  'auth.magic.missing_token': 'V odkazu chybí přihlašovací kód.',
  'auth.magic.request_done_body': 'Pokud k zadanému e-mailu existuje účet, právě jsme tam poslali přihlašovací odkaz.',

  // Auth — common
  'auth.common.session_pending': 'Pokračujeme…',
  'auth.common.required_field': 'Toto pole je povinné.',
  'auth.common.invalid_email': 'Zadejte platný e-mail.',
  'auth.common.password_too_short': 'Heslo musí mít alespoň 10 znaků.',

  // Navigation
  'nav.catalog': 'Katalog',
  'nav.how_it_works': 'Jak to funguje',
  'nav.for_makers': 'Pro makery',
  'nav.dashboard': 'Přehled',
  'nav.logout': 'Odhlásit se',

  // Catalog
  'catalog.title': 'Katalog výrobců',
  'catalog.empty': 'Žádní výrobci neodpovídají vašemu filtru.',

  // Orders
  'order.state.pending_payment': 'Čeká na platbu',
  'order.state.paid': 'Zaplaceno',
  'order.state.accepted': 'Přijato',
  'order.state.shipped': 'Odesláno',
  'order.state.delivered': 'Doručeno',
  'order.state.completed': 'Dokončeno',
  'order.state.cancelled': 'Zrušeno',
  'order.state.refunded': 'Vráceno',
  'order.state.disputed': 'V řízení',
} as const;

export type MessageKey = keyof typeof messages;

/**
 * Look up a Czech string by key, optionally substituting `{name}` placeholders.
 *
 * @example
 *   t('common.app_name')                       // "Makables"
 *   t('order.state.pending_payment')           // "Čeká na platbu"
 *   t('auth.login.title')                      // "Přihlášení"
 */
export function t(key: MessageKey, params?: Record<string, string | number>): string {
  let value: string = messages[key];
  if (params) {
    for (const [name, replacement] of Object.entries(params)) {
      value = value.replaceAll(`{${name}}`, String(replacement));
    }
  }
  return value;
}
