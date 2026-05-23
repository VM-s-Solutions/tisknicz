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

  // Auth
  'auth.login.title': 'Přihlášení',
  'auth.login.email': 'E-mail',
  'auth.login.password': 'Heslo',
  'auth.login.submit': 'Přihlásit se',
  'auth.login.forgot_password': 'Zapomněli jste heslo?',
  'auth.login.magic_link': 'Přihlásit se odkazem v e-mailu',
  'auth.register.title': 'Vytvořit účet',

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
