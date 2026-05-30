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

  // Dashboard — customer profile
  'dashboard.customer.profile.title': 'Můj profil',
  'dashboard.customer.profile.section_personal': 'Osobní údaje',
  'dashboard.customer.profile.section_password': 'Heslo',
  'dashboard.customer.profile.full_name': 'Jméno a příjmení',
  'dashboard.customer.profile.phone': 'Telefon',
  'dashboard.customer.profile.phone_placeholder': '+420 …',
  'dashboard.customer.profile.email_readonly': 'E-mail',
  'dashboard.customer.profile.email_change_hint': 'Pro změnu e-mailu kontaktujte podporu.',
  'dashboard.customer.profile.save': 'Uložit změny',
  'dashboard.customer.profile.saving': 'Ukládám…',
  'dashboard.customer.profile.saved': 'Změny uloženy.',
  'dashboard.customer.profile.current_password': 'Současné heslo',
  'dashboard.customer.profile.new_password': 'Nové heslo',
  'dashboard.customer.profile.change_password': 'Změnit heslo',
  'dashboard.customer.profile.password_changed': 'Heslo bylo změněno.',
  'dashboard.customer.profile.password_wrong': 'Současné heslo není správné.',
  'dashboard.customer.profile.logout': 'Odhlásit se',

  // Dashboard — maker profile
  'dashboard.maker.profile.title': 'Profil výrobce',
  'dashboard.maker.profile.section_company': 'Firemní údaje (ARES)',
  'dashboard.maker.profile.section_about': 'O výrobci',
  'dashboard.maker.profile.section_pickup': 'Osobní odběr',
  'dashboard.maker.profile.section_bank': 'Bankovní účet',
  'dashboard.maker.profile.company_name': 'Název firmy',
  'dashboard.maker.profile.ico': 'IČO',
  'dashboard.maker.profile.vat_id': 'DIČ',
  'dashboard.maker.profile.legal_form': 'Právní forma',
  'dashboard.maker.profile.readonly_hint': 'Firemní údaje pocházejí z rejstříku ARES a může je aktualizovat pouze administrátor.',
  'dashboard.maker.profile.verified': 'Účet je ověřen administrátorem',
  'dashboard.maker.profile.not_verified': 'Účet čeká na ověření administrátorem',
  'dashboard.maker.profile.snapshot_stale': 'Údaje z ARES jsou starší. Administrátor je obnoví při ověření.',
  'dashboard.maker.profile.bio': 'Krátký popis (max 500 znaků)',
  'dashboard.maker.profile.bank_account': 'Bankovní účet',
  'dashboard.maker.profile.bank_account_placeholder': 'např. 2000145399/0100',
  'dashboard.maker.profile.bank_account_invalid': 'Bankovní účet není v platném tvaru.',
  'dashboard.maker.profile.pickup_enabled': 'Nabízím osobní odběr',
  'dashboard.maker.profile.pickup_note': 'Poznámka k odběru',
  'dashboard.maker.profile.save': 'Uložit změny',
  'dashboard.maker.profile.saving': 'Ukládám…',
  'dashboard.maker.profile.saved': 'Změny uloženy.',

  // Navigation
  'nav.catalog': 'Katalog',
  'nav.how_it_works': 'Jak to funguje',
  'nav.for_makers': 'Pro makery',
  'nav.dashboard': 'Přehled',
  'nav.logout': 'Odhlásit se',

  // Catalog — page
  'catalog.title': 'Katalog výrobců',
  'catalog.subtitle': 'Najděte si výrobce ve svém okolí. Filtrujte podle kategorie, města nebo hodnocení.',

  // Catalog — filters
  'catalog.filter.heading': 'Filtry',
  'catalog.filter.category': 'Kategorie',
  'catalog.filter.category_any': 'Všechny kategorie',
  'catalog.filter.city': 'Město',
  'catalog.filter.city_placeholder': 'např. Praha',
  'catalog.filter.min_rating': 'Minimální hodnocení',
  'catalog.filter.min_rating_any': 'Bez omezení',
  'catalog.filter.min_rating_stars': '{stars}+ hvězd',
  'catalog.filter.apply': 'Použít filtry',
  'catalog.filter.reset': 'Vymazat filtry',

  // Catalog — category labels (T-0040 launch slugs)
  'catalog.category.cat-3d-tisk': '3D tisk',
  'catalog.category.cat-klasicky-tisk': 'Klasický tisk',
  'catalog.category.cat-potisk-textilu': 'Potisk textilu',
  'catalog.category.cat-laser-cnc': 'Laser & CNC',
  'catalog.category.cat-velkoformat': 'Velkoformát',
  'catalog.category.cat-handmade': 'Handmade',

  // Catalog — card
  'catalog.card.verified': 'Ověřený výrobce',
  'catalog.card.orders': '{count} objednávek',
  'catalog.card.rating_none': 'Bez hodnocení',
  'catalog.card.rating_count': '({count})',

  // Catalog — empty
  'catalog.empty.title': 'Žádní výrobci neodpovídají vašemu filtru',
  'catalog.empty.description': 'Zkuste rozšířit kritéria nebo vymazat filtry.',
  'catalog.empty.reset': 'Vymazat filtry',

  // Catalog — error
  'catalog.error.title': 'Katalog se nepodařilo načíst',
  'catalog.error.retry': 'Zkusit znovu',

  // Catalog — pagination
  'catalog.pagination.previous': 'Předchozí',
  'catalog.pagination.next': 'Další',
  'catalog.pagination.page_of': 'Stránka {page} z {total}',
  'catalog.pagination.results': '{count} výrobců',

  // Legacy short keys kept for backward compatibility
  'catalog.empty': 'Žádní výrobci neodpovídají vašemu filtru.',

  // Catalog — maker profile page (T-0047, US-customer-0008)
  'catalog.maker.verified': 'Ověřený výrobce',
  'catalog.maker.personal_pickup_badge': 'Osobní odběr',
  'catalog.maker.stats.rating': '{rating} ({count} hodnocení)',
  'catalog.maker.stats.rating_none': 'Bez hodnocení',
  'catalog.maker.stats.orders': '{count} dokončených objednávek',
  'catalog.maker.pickup.heading': 'Osobní odběr',
  'catalog.maker.products.heading': 'Produkty',
  'catalog.maker.products.empty': 'Tento výrobce zatím nemá žádné aktivní produkty.',
  'catalog.maker.reviews.heading': 'Hodnocení zákazníků',
  'catalog.maker.reviews.empty': 'Tento výrobce zatím nemá žádná hodnocení.',
  'catalog.maker.error.title': 'Profil se nepodařilo načíst',
  'catalog.maker.error.body': 'Zkuste prosím obnovit stránku za chvíli.',
  'catalog.maker.not_found.title': 'Výrobce nenalezen',
  'catalog.maker.not_found.body': 'Tento profil neexistuje nebo už není dostupný.',
  'catalog.maker.not_found.back_to_catalog': 'Zpět do katalogu',
  'catalog.maker.metadata.fallback_description': 'Profil výrobce na Makables.',
  'catalog.maker.metadata.title_suffix': 'Makables',

  // Catalog — product card (T-0047)
  'catalog.product.price.from': 'od {price}',
  'catalog.product.price.on_request': 'Na poptávku',
  'catalog.product.image_alt': 'Fotografie produktu {title}',
  'catalog.product.no_image': 'Bez fotografie',

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
