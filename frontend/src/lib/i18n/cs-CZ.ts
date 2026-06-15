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
  // Czech plural-neutral phrasing (T-0047 Copilot review): the count
  // interpolation skips the genitive-plural trap (1 → one, 2-4 → few,
  // 0/5+ → many, fractional → other). Until t() learns Intl.PluralRules
  // every {count} label takes a "Label: N" shape that's grammatical for
  // every count.
  'catalog.card.verified': 'Ověřený výrobce',
  'catalog.card.orders': 'Objednávek: {count}',
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
  'catalog.pagination.results': 'Výrobců: {count}',

  // Legacy short keys kept for backward compatibility
  'catalog.empty': 'Žádní výrobci neodpovídají vašemu filtru.',

  // Catalog — maker profile page (T-0047, US-customer-0008)
  'catalog.maker.verified': 'Ověřený výrobce',
  'catalog.maker.personal_pickup_badge': 'Osobní odběr',
  'catalog.maker.stats.rating': '{rating} (hodnocení: {count})',
  'catalog.maker.stats.rating_none': 'Bez hodnocení',
  'catalog.maker.stats.orders': 'Dokončených objednávek: {count}',
  'catalog.maker.pickup.heading': 'Osobní odběr',
  'catalog.maker.products.heading': 'Produkty',
  'catalog.maker.products.empty': 'Tento výrobce zatím nemá žádné aktivní produkty.',
  'catalog.maker.reviews.heading': 'Hodnocení zákazníků',
  'catalog.maker.reviews.empty': 'Tento výrobce zatím nemá žádná hodnocení.',
  'catalog.maker.error.title': 'Profil se nepodařilo načíst',
  'catalog.maker.error.body': 'Zkuste prosím obnovit stránku za chvíli.',
  'catalog.maker.not_found.title': 'Výrobce nenalezen',
  'catalog.maker.not_found.body': 'Tento profil neexistuje nebo už není dostupný.',
  // Shared by the not-found page and the happy-path profile footer, so
  // it lives at the maker namespace root rather than under not_found.*
  // (T-0047 Copilot review).
  'catalog.maker.back_to_catalog': 'Zpět do katalogu',
  'catalog.maker.metadata.fallback_description': 'Profil výrobce na Makables.',
  'catalog.maker.metadata.title_suffix': 'Makables',

  // Catalog — product card (T-0047)
  'catalog.product.price.from': 'od {price}',
  'catalog.product.price.on_request': 'Na poptávku',
  'catalog.product.image_alt': 'Fotografie produktu {title}',
  'catalog.product.no_image': 'Bez fotografie',

  // Catalog — product detail page (T-0048, US-customer-0009).
  // Plural-neutral phrasing rule from the catalog card block above
  // applies here too — keep any future {count} keys in the "Label: N"
  // shape until t() learns Intl.PluralRules.
  'catalog.product_detail.heading.by_maker': 'Vyrobeno {maker}',
  'catalog.product_detail.cta.order': 'Objednat',
  'catalog.product_detail.weight': 'Hmotnost: {value}',
  'catalog.product_detail.description.heading': 'Popis',
  'catalog.product_detail.gallery.thumbnail_aria': 'Náhled {n}',
  'catalog.product_detail.gallery.no_image': 'Bez fotografie',
  'catalog.product_detail.error.title': 'Produkt se nepodařilo načíst',
  'catalog.product_detail.error.body': 'Zkuste prosím obnovit stránku za chvíli.',
  'catalog.product_detail.not_found.title': 'Produkt nenalezen',
  'catalog.product_detail.not_found.body': 'Tento produkt neexistuje nebo už není dostupný.',
  'catalog.product_detail.metadata.fallback_description': 'Detail produktu na Makables.',

  // Dashboard — maker products (T-0049).
  // Plural-neutral phrasing rule from the catalog block above applies
  // here too — every {count} key uses the "Label: N" shape until t()
  // learns Intl.PluralRules.
  'dashboard.maker.products.title': 'Mé produkty',
  'dashboard.maker.products.subtitle': 'Spravujte své produkty — přidávejte nové, upravujte ceny a fotografie.',
  'dashboard.maker.products.metadata.title': 'Mé produkty — Makables',
  'dashboard.maker.products.metadata.description': 'Správa produktů ve vašem obchodě na Makables.',
  'dashboard.maker.products.cta.create': 'Přidat produkt',
  'dashboard.maker.products.count': 'Produktů: {count}',

  'dashboard.maker.products.empty.title': 'Zatím jste nepřidali žádné produkty',
  'dashboard.maker.products.empty.description': 'Začněte vytvořením prvního produktu — fotografie přidáte hned po uložení.',
  'dashboard.maker.products.empty.cta': 'Přidat první produkt',

  'dashboard.maker.products.error.title': 'Produkty se nepodařilo načíst',
  'dashboard.maker.products.error.body': 'Zkuste prosím obnovit stránku za chvíli.',
  'dashboard.maker.products.error.retry': 'Zkusit znovu',

  'dashboard.maker.products.card.image_alt': 'Fotografie produktu {title}',
  'dashboard.maker.products.card.no_image': 'Bez fotografie',
  'dashboard.maker.products.card.image_count': 'Fotografií: {count}',
  'dashboard.maker.products.card.weight': 'Hmotnost: {value}',
  'dashboard.maker.products.card.created': 'Vytvořeno {date}',
  'dashboard.maker.products.badge.active': 'Aktivní',
  'dashboard.maker.products.badge.inactive': 'Neaktivní',
  'dashboard.maker.products.actions.edit': 'Upravit',
  'dashboard.maker.products.actions.delete': 'Smazat',

  'dashboard.maker.products.create.title': 'Nový produkt',
  'dashboard.maker.products.create.subtitle': 'Vyplňte základní údaje. Fotografie přidáte na další stránce po uložení.',
  'dashboard.maker.products.create.metadata.title': 'Nový produkt — Makables',
  'dashboard.maker.products.create.back': 'Zpět na produkty',

  'dashboard.maker.products.edit.title': 'Úprava produktu',
  'dashboard.maker.products.edit.metadata.title': 'Úprava produktu — Makables',
  'dashboard.maker.products.edit.back': 'Zpět na produkty',
  'dashboard.maker.products.edit.inactive_banner': 'Tento produkt je neaktivní a není viditelný na vašem veřejném profilu.',
  'dashboard.maker.products.edit.not_found.title': 'Produkt nenalezen',
  'dashboard.maker.products.edit.not_found.body': 'Tento produkt neexistuje, byl odstraněn nebo k němu nemáte přístup.',
  'dashboard.maker.products.edit.error.title': 'Produkt se nepodařilo načíst',
  'dashboard.maker.products.edit.error.body': 'Zkuste prosím obnovit stránku za chvíli.',

  'dashboard.maker.products.form.section_basic': 'Základní údaje',
  'dashboard.maker.products.form.section_pricing': 'Cena a hmotnost',
  'dashboard.maker.products.form.field.title': 'Název produktu',
  'dashboard.maker.products.form.field.description': 'Popis',
  'dashboard.maker.products.form.field.description_help': 'Volitelný popis. Krátký, výstižný — co produkt umí a komu se hodí.',
  'dashboard.maker.products.form.field.category': 'Kategorie',
  'dashboard.maker.products.form.field.category_placeholder': 'Vyberte kategorii',
  'dashboard.maker.products.form.field.price_type': 'Typ ceny',
  'dashboard.maker.products.form.field.price_amount': 'Cena',
  'dashboard.maker.products.form.field.price_amount_help': 'Částka v Kč. U "Na poptávku" je pole nepovinné — odešle se 0 Kč jako informační údaj, finální cenu doladíte se zákazníkem.',
  'dashboard.maker.products.form.field.weight': 'Hmotnost',
  'dashboard.maker.products.form.field.weight_help': 'Hmotnost v gramech — používá se pro výpočet poštovného.',
  'dashboard.maker.products.form.price_type.Fixed': 'Pevná cena',
  'dashboard.maker.products.form.price_type.From': 'Od (orientační)',
  'dashboard.maker.products.form.price_type.OnRequest': 'Na poptávku',
  'dashboard.maker.products.form.submit.create': 'Vytvořit produkt',
  'dashboard.maker.products.form.submit.update': 'Uložit změny',
  'dashboard.maker.products.form.submit.saving': 'Ukládám…',
  'dashboard.maker.products.form.success.updated': 'Změny uloženy.',
  'dashboard.maker.products.form.error.generic': 'Produkt se nepodařilo uložit. Zkuste to prosím znovu.',
  'dashboard.maker.products.form.error.validation_summary': 'Některé údaje neprošly validací. Opravte označená pole.',

  'dashboard.maker.products.images.title': 'Fotografie produktu',
  'dashboard.maker.products.images.description': 'Až 10 fotografií ve formátu JPEG, PNG nebo WebP, každá do 5 MB.',
  'dashboard.maker.products.images.empty': 'Zatím nejsou nahrané žádné fotografie.',
  'dashboard.maker.products.images.upload_button': 'Nahrát fotografii',
  'dashboard.maker.products.images.uploading': 'Nahrávám…',
  'dashboard.maker.products.images.remove': 'Odebrat',
  'dashboard.maker.products.images.removing': 'Odebírám…',
  'dashboard.maker.products.images.image_alt': 'Fotografie produktu {n}',
  'dashboard.maker.products.images.error.too_large': 'Fotografie je příliš velká. Maximum je 5 MB.',
  'dashboard.maker.products.images.error.unsupported_type': 'Nepodporovaný formát fotografie. Použijte JPEG, PNG nebo WebP.',
  'dashboard.maker.products.images.error.invalid': 'Fotografii se nepodařilo nahrát. Zkontrolujte soubor a zkuste to znovu.',
  'dashboard.maker.products.images.error.limit_reached': 'Dosáhli jste limitu 10 fotografií u jednoho produktu. Před nahráním další nějakou odeberte.',
  'dashboard.maker.products.images.error.remove_failed': 'Fotografii se nepodařilo odebrat. Zkuste to prosím znovu.',

  'dashboard.maker.products.delete.button': 'Smazat produkt',
  'dashboard.maker.products.delete.confirm.title': 'Smazat produkt?',
  'dashboard.maker.products.delete.confirm.body': 'Produkt přestane být viditelný ve veřejném katalogu. Zákazníci s ním nebudou moci vytvářet nové objednávky.',
  'dashboard.maker.products.delete.confirm.confirm_button': 'Ano, smazat',
  'dashboard.maker.products.delete.confirm.cancel_button': 'Zrušit',
  'dashboard.maker.products.delete.error': 'Produkt se nepodařilo smazat. Zkuste to prosím znovu.',

  'dashboard.maker.products.pagination.previous': 'Předchozí',
  'dashboard.maker.products.pagination.next': 'Další',
  'dashboard.maker.products.pagination.page_of': 'Stránka {page} z {total}',

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

  // T-0063 CreateOrder error codes (parity with BusinessErrorMessage).
  // PM to review on the PR — UX may refine the Czech wording.
  'order.invalidQuantity': 'Množství musí být 1.',
  'product.notActive': 'Tento výrobek již není k dispozici.',
  'maker.deactivated': 'Tento výrobce momentálně nepřijímá objednávky.',
  'maker.notVerified': 'Tento výrobce ještě nebyl ověřen a nemůže přijímat objednávky.',
  'maker.personalPickupDisabled': 'Tento výrobce osobní odběr nenabízí.',

  // T-0064 Order attachments (parity with BusinessErrorMessage).
  // PM/UX to refine on PR review.
  'order.attachmentLimitReached': 'K této objednávce lze přiložit nejvýše 10 souborů.',
  'order.stateForbidsAttachment': 'V tomto stavu objednávky již nelze přidávat přílohy.',
  'order.attachmentNotFound': 'Tato příloha neexistuje nebo k ní nemáte přístup.',

  // T-0065 Comgate payment session (parity with BusinessErrorMessage).
  // PM/UX to refine on PR review.
  'payment.providerUnavailable': 'Platební brána je momentálně nedostupná. Zkuste to prosím za pár minut.',
  'payment.providerRejected': 'Platba byla zamítnuta. Zkontrolujte údaje a zkuste to znovu.',
  'payment.providerMisconfigured': 'Platba dočasně není možná z technických důvodů.',
  'payment.providerNotRegistered': 'Platba pro tuto zemi není podporována.',
  'payment.unknownError': 'Nastala neznámá chyba při zpracování platby. Zkuste to prosím znovu.',
  'order.invalidStateForPayment': 'Tuto objednávku již nelze platit.',
  'order.paymentAlreadyCaptured': 'Tato objednávka už byla zaplacena.',

  // T-0066 Comgate webhook (parity with BusinessErrorMessage).
  // These keys surface in the admin audit log (T-0118), not customer-
  // facing — the webhook is server-to-server. PM/UX may refine wording.
  'payment.webhook.malformed': 'Webhook od platební brány nemá očekávaný formát.',
  'payment.webhook.ipRejected': 'Webhook přišel z neoprávněné IP adresy.',
  'payment.webhook.refIdMismatch': 'Referenční ID ve webhooku se neshoduje s objednávkou.',

  // Email pipeline — consumer-side codes that surface in the admin audit
  // log only (never customer-facing). T-0067.
  'email.orderPayloadMalformed': 'Vnitřní chyba při generování e-mailu k objednávce. Tým byl informován.',
  // FK-invariant codes. Surface only in the admin audit log (the customer
  // never sees these — a webhook 5xx is the closest UX). T-0067 reviewer M-2/M-3.
  'maker.userMissing': 'Účet poskytovatele nebyl nalezen. Tým byl informován.',
  'order.customerUserMissing': 'Účet zákazníka nebyl nalezen. Tým byl informován.',

  // Invoice pipeline (T-0068a + T-0068b). All four codes are admin / log
  // surface — the customer never sees them directly (the GenerateInvoice
  // Function consumes the invoice.generate outbox row; a failure parks
  // the row for admin attention rather than surfacing to the checkout
  // UI). Kept for parity with BusinessErrorMessage so admin dashboards
  // can show a localised string.
  'invoice.blobPathAlreadySet':
    'Faktura už má přiřazenou cestu k PDF. Tým byl informován.',
  'invoice.invoicingModeNotImplemented':
    'Tento režim fakturace zatím není podporován. Tým byl informován.',
  'invoice.renderFailed':
    'Generování PDF faktury selhalo. Tým byl informován.',
  'invoice.blobUploadFailed':
    'Nahrání PDF faktury do úložiště selhalo. Tým byl informován.',
  // T-0069 invoice email attachment codes. Same admin / log surface as
  // the other invoice.* codes — the customer never sees these (a Transient
  // re-delivers the email; a Permanent stalls the row for ops).
  'invoice.notYetRendered':
    'Faktura ještě nebyla vygenerována. Email bude odeslán znovu.',
  'invoice.pdfAttachmentDownloadFailed':
    'Stažení PDF faktury selhalo. Tým byl informován.',
  'invoice.pdfAttachmentTooLarge':
    'PDF faktury překračuje maximální velikost přílohy. Tým byl informován.',
  // T-0070 shipping carrier codes. Customer-facing (carrierUnavailable +
  // addressIdNotFound) and admin/log-facing (invalidWeight +
  // configurationError) surfaces. Mirror of the payment-provider codes.
  'shipping.carrierUnavailable':
    'Doprava je momentálně nedostupná. Zkuste to prosím za chvíli.',
  'shipping.invalidWeight':
    'Hmotnost zásilky překračuje povolený limit. Tým byl informován.',
  'shipping.addressIdNotFound':
    'Vybrané výdejní místo již není dostupné. Vyberte prosím jiné.',
  'shipping.configurationError':
    'Konfigurace dopravce není správně nastavena. Tým byl informován.',
  // T-0072: maker called the wrong shipping endpoint for this order's
  // shipping method. Frontend dashboard surfaces this on the order detail.
  'shipping.methodNotEligible':
    'Tato objednávka není zásilkovnová — použijte tlačítko Předat osobně.',

  // T-0079 order-message thread error codes (parity with
  // BusinessErrorMessage). PM/UX to refine on PR review.
  'order.message.bodyEmpty': 'Zpráva nesmí být prázdná.',
  'order.message.bodyTooLong': 'Zpráva může mít nejvýše 2000 znaků.',
  'order.message.notAllowedInState':
    'Zprávy lze odesílat až po zaplacení objednávky.',

  // Order lifecycle error codes (parity with BusinessErrorMessage),
  // consumed by the tracking detail (T-0086b — verified missing at the
  // §C parity check, added here). PM/UX to refine on PR review.
  'order.notFound': 'Tato objednávka neexistuje nebo k ní nemáte přístup.',
  'order.invalidTransition':
    'Tuto akci nelze v aktuálním stavu objednávky provést.',

  // T-0105 admin refund error codes (parity with BusinessErrorMessage).
  // Admin-surface only (T-0118 refund UI). PM/UX to refine on PR review.
  'payment.refund.invalidState':
    'Objednávku v tomto stavu nelze refundovat.',
  'payment.refund.amountExceedsRemaining':
    'Částka překračuje zbývající refundovatelnou částku objednávky.',
  'payment.refund.postPayoutAckRequired':
    'Objednávka už byla vyplacena výrobci — refundaci je nutné výslovně potvrdit.',
  'payment.refund.noProviderRef':
    'Objednávka nemá záznam o platbě — není co refundovat.',

  // T-0106 dispute error codes (parity with BusinessErrorMessage).
  // PM/UX to refine on PR review.
  'order.dispute.categoryNotAllowed':
    'Tuto kategorii reklamace nelze zvolit — je vyhrazena dopravci.',
  'order.dispute.notOpen':
    'K této objednávce není otevřená žádná reklamace.',

  // T-0100 review error codes (parity with BusinessErrorMessage —
  // resolveErrorMessage maps the dotted code 1:1). Vykání on the customer
  // surfaces. PM/UX to refine on PR review.
  'review.alreadyExists': 'K této objednávce už recenze existuje.',
  'review.orderNotDelivered': 'Recenzi lze přidat až po doručení objednávky.',
  'review.ratingOutOfRange': 'Hodnocení musí být 1 až 5 hvězdiček.',
  'review.bodyTooLong': 'Text recenze může mít nejvýše 1000 znaků.',
  'review.replyEmpty': 'Odpověď nesmí být prázdná.',
  'review.replyTooLong': 'Odpověď může mít nejvýše 500 znaků.',
  'review.notFound': 'Recenze nebyla nalezena.',
  // Admin / ops surface only — the outbox row parks until the
  // ADMIN_NOTIFICATION_EMAIL config lands.
  'email.adminRecipientNotConfigured':
    'E-mail pro administrátorská upozornění není nastaven. Tým byl informován.',

  // T-0107 manual state-change error codes (parity with
  // BusinessErrorMessage). Admin-surface only (T-0118 UI) — the Czech
  // copy names the sanctioned action per US-admin-0010 AC-2.
  'order.manualTransition.notAllowed':
    'Tento ruční přechod stavu není povolen.',
  'order.manualTransition.useRefundOrder':
    'Tento přechod není povolen — použijte refundaci objednávky.',
  'order.manualTransition.useOpenDispute':
    'Tento přechod není povolen — použijte otevření reklamace.',
  'order.manualTransition.useResolveDispute':
    'Objednávka je v reklamaci — použijte vyřízení reklamace.',
  'order.manualTransition.useMarkPayoutBatchCompleted':
    'Tento přechod není povolen — dokončení proběhne vyplacením výrobce.',
  'order.manualTransition.paidRequiresProviderRef':
    'Objednávku nelze označit jako zaplacenou — chybí záznam platby od platební brány.',

  // T-0102a/b payout-batch error codes (parity with BusinessErrorMessage).
  // Admin-surface only (T-0118 payout UI). PM/UX to refine on PR review.
  'payoutBatch.empty':
    'Žádné objednávky připravené k výplatě.',
  'payoutBatch.weekAlreadyProcessed':
    'Výplatní dávka pro tento týden už byla zpracována.',
  'payoutBatch.alreadyOpen':
    'Výplatní dávka se právě zpracovává. Zkuste to prosím za chvíli.',
  'payoutBatch.currencyMismatch':
    'Objednávky v dávce nemají jednotnou měnu. Kontaktujte podporu.',
  'payoutBatch.notFound':
    'Výplatní dávka nebyla nalezena.',
  'payoutBatch.csvNotReady':
    'CSV soubor výplatní dávky se ještě generuje. Zkuste to prosím za chvíli.',
  'payoutBatch.notProcessing':
    'Tuto výplatní dávku nelze dokončit — není ve stavu zpracování.',
  // Admin / log surface (mirror of invoice.blobPathAlreadySet) — the CSV
  // formatter is deterministic so a real overwrite is a programmer error.
  'payoutBatch.csvPathAlreadySet':
    'Cesta k CSV souboru výplatní dávky už byla nastavena. Tým byl informován.',

  // T-0109 admin outbox retry/acknowledge codes (parity with
  // BusinessErrorMessage). Admin-surface only (T-0118 outbox UI).
  'outbox.rowNotFound': 'Tato fronta událostí už neexistuje.',
  'outbox.alreadyProcessed':
    'Tato událost už byla zpracována — není co opakovat.',

  // T-0108 admin country-configuration codes (parity with
  // BusinessErrorMessage). Admin-surface only (T-0118 countries UI).
  'country.providerNotRegistered':
    'Zadaný kód poskytovatele není zaregistrován v systému.',
  'country.providerConfirmationMismatch':
    'Pro potvrzení změny poskytovatele přepište nový kód přesně.',
  'countryConfiguration.notFound':
    'Konfigurace pro tuto zemi nebyla nalezena.',

  // T-0110 admin GDPR-erasure codes (parity with BusinessErrorMessage).
  // Admin-surface only (T-0118 erase UI).
  'user.notFound': 'Uživatel nebyl nalezen.',
  'user.deleteConfirmationMismatch':
    'Zadaný e-mail neodpovídá uživateli — smazání nebylo potvrzeno.',
  'user.cannotDeleteWithInFlightOrders':
    'Uživatele nelze smazat — má rozpracované objednávky. Nejprve je vyřešte.',

  // Error-code parity keys consumed by the checkout-flow bundle
  // (T-0084a/b). auth.emailNotConfirmed comes from the customer host's
  // RequireEmailConfirmedMiddleware 403; file.* from the T-0064
  // attachment validator. PM/UX to refine on PR review.
  'auth.emailNotConfirmed':
    'Váš e-mail dosud nebyl potvrzen. Zkontrolujte prosím schránku.',
  'file.invalid':
    'Soubor se nepodařilo nahrát. Zkontrolujte jej a zkuste to znovu.',
  'file.tooLarge': 'Soubor je příliš velký. Maximum je 10 MB.',
  'file.unsupportedType':
    'Nepodporovaný formát souboru. Použijte PDF, JPEG, PNG nebo WebP.',

  // Checkout — order form at /objednavka (T-0084a, US-customer-0010/0011).
  // Vykání throughout (customer audience). The {count} keys follow the
  // plural-neutral "Label: N" convention from the catalog block above.
  'checkout.title': 'Objednávka',
  'checkout.subtitle':
    'Zkontrolujte údaje a odešlete objednávku. Platba proběhne v dalším kroku.',
  'checkout.metadata.title': 'Objednávka — Makables',
  'checkout.invalidLink.title': 'Neplatný odkaz na objednávku',
  'checkout.invalidLink.cta': 'Přejít do katalogu',
  'checkout.loadError.title': 'Objednávku nyní nelze připravit',
  'checkout.loadError.body': 'Zkuste prosím obnovit stránku za chvíli.',

  'checkout.contact.legend': 'Kontaktní údaje',
  'checkout.contact.name': 'Jméno a příjmení',
  'checkout.contact.namePlaceholder': 'např. Jan Novák',
  'checkout.contact.email': 'E-mail',
  'checkout.contact.phone': 'Telefon',
  'checkout.contact.phonePlaceholder': '+420 777 123 456',
  'checkout.contact.notes': 'Poznámka pro výrobce (nepovinné)',
  'checkout.contact.notesCounter': 'Znaků: {count}/{max}',

  'checkout.validation.name': 'Zadejte jméno a příjmení (2–100 znaků).',
  'checkout.validation.email': 'Zadejte platný e-mail.',
  'checkout.validation.phone':
    'Zadejte platné české telefonní číslo, např. +420 777 123 456.',
  'checkout.validation.notes': 'Poznámka může mít nejvýše 2000 znaků.',

  'checkout.shipping.legend': 'Doprava',
  'checkout.shipping.zasilkovna': 'Zásilkovna — výdejní místo',
  'checkout.shipping.personalPickup': 'Osobní odběr u výrobce',
  'checkout.shipping.personalPickupDisabled':
    'Tento výrobce osobní odběr nenabízí.',
  'checkout.shipping.pickupInfo':
    'Osobní odběr ve městě {city}. Přesné místo a čas si domluvíte s výrobcem po zaplacení objednávky.',
  'checkout.shipping.unavailable':
    'Momentálně není dostupný žádný způsob dopravy. Zkuste to prosím později.',

  'checkout.pickupPoint.choose': 'Vybrat výdejní místo',
  'checkout.pickupPoint.change': 'Změnit',
  'checkout.pickupPoint.chosen': 'Vybrané výdejní místo: {name}',
  'checkout.pickupPoint.required': 'Vyberte prosím výdejní místo.',
  'checkout.widget.error':
    'Mapu výdejních míst se nepodařilo načíst. Zásilkovna je dočasně nedostupná.',
  'checkout.widget.retry': 'Zkusit znovu',

  'checkout.attachments.label': 'Přílohy (nepovinné)',
  'checkout.attachments.hint':
    'Až 10 souborů ve formátu PDF, JPEG, PNG nebo WebP, každý do 10 MB.',
  'checkout.attachments.add': 'Vybrat soubory',
  'checkout.attachments.remove': 'Odebrat',
  'checkout.attachments.statePending': 'Čeká',
  'checkout.attachments.stateUploading': 'Nahrává se…',
  'checkout.attachments.stateDone': 'Hotovo',
  'checkout.attachments.stateFailed': 'Chyba',
  'checkout.attachments.rejectedType':
    'Soubor {name} má nepodporovaný formát. Použijte PDF, JPEG, PNG nebo WebP.',
  'checkout.attachments.rejectedSize':
    'Soubor {name} je příliš velký. Maximum je 10 MB.',
  'checkout.attachments.rejectedCount':
    'K objednávce lze přiložit nejvýše 10 souborů.',

  'checkout.summary.product': 'Souhrn objednávky',
  'checkout.summary.shippingNote':
    'Cena dopravy bude vyčíslena v souhrnu objednávky po odeslání.',
  'checkout.summary.totalNote':
    'Konečnou cenu včetně dopravy uvidíte před platbou.',

  'checkout.submit': 'Odeslat objednávku',
  'checkout.submitting': 'Odesílám…',
  'checkout.uploadProgress': 'Nahrávání příloh: {done}/{total}',
  'checkout.emailNotConfirmedHint':
    'Nový potvrzovací odkaz si můžete poslat ze svého profilu.',

  // Pre-payment order page at /objednavka/[id] (T-0084b,
  // US-customer-0010 AC-2/AC-3). Vykání throughout.
  'order.page.title': 'Objednávka {orderNumber}',
  'order.page.metadata.title': 'Objednávka — Makables',
  'order.page.loadError': 'Objednávku se nepodařilo načíst',
  'order.page.loadErrorBody': 'Zkuste prosím obnovit stránku za chvíli.',
  'order.page.loadErrorRetry': 'Zkusit znovu',
  'order.page.notFound.title': 'Objednávka nenalezena',
  'order.page.notFound.body':
    'Tato objednávka neexistuje nebo k ní nemáte přístup.',

  'order.page.payCta': 'Zaplatit',
  'order.page.paying': 'Přesměrování na platební bránu…',
  'order.page.expiresNotice':
    'Nezaplacené objednávky rušíme po 24 hodinách. Zaplaťte prosím do {deadline}.',

  'order.page.breakdown.product': 'Výrobek',
  'order.page.breakdown.shipping': 'Doprava',
  'order.page.breakdown.vat': 'DPH {rate} %',
  'order.page.breakdown.total': 'Celkem',
  'order.page.breakdown.contact': 'Kontaktní údaje',
  'order.page.breakdown.customOrderFallback': 'Vlastní zakázka',
  'order.page.shippingMethod.zasilkovna': 'Zásilkovna — výdejní místo',
  'order.page.shippingMethod.personalPickup': 'Osobní odběr',

  'order.page.attachments.heading': 'Přílohy',
  'order.page.attachments.addMore': 'Přidat soubory',
  'order.page.attachments.retry': 'Zkusit znovu',
  'order.page.attachments.uploading': 'Nahrává se…',
  'order.page.attachments.done': 'Nahráno',
  'order.page.attachments.failed': 'Nahrání se nezdařilo',
  'order.page.attachments.failedHandoffAlert':
    'Souborů, které se nepodařilo nahrát: {count}. Přidejte je prosím znovu níže.',

  'order.page.banner.detailComing':
    'Kompletní detail objednávky pro vás připravujeme. O každé změně vás budeme informovat e-mailem.',
  'order.page.banner.backToCatalog': 'Zpět do katalogu',

  // Customer dashboard order list at /dashboard/zakaznik/objednavky
  // (T-0086a, US-customer-0016). Vykání throughout; {count} keys follow
  // the plural-neutral "Label: N" convention from the catalog block.
  'customer.orders.title': 'Moje objednávky',
  'customer.orders.subtitle': 'Přehled vašich objednávek — stav, zprávy od výrobce a detail každé zakázky.',
  'customer.orders.metadata.title': 'Moje objednávky — Makables',
  'customer.orders.count': 'Objednávek: {count}',
  'customer.orders.customOrder': 'Vlastní zakázka',

  'customer.orders.filter.state': 'Stav',
  'customer.orders.filter.state_any': 'Všechny stavy',
  'customer.orders.filter.dateFrom': 'Od data',
  'customer.orders.filter.dateTo': 'Do data',
  'customer.orders.filter.sort': 'Řazení',
  'customer.orders.filter.reset': 'Vymazat filtry',

  'customer.orders.sort.CreatedAtDesc': 'Nejnovější',
  'customer.orders.sort.CreatedAtAsc': 'Nejstarší',
  'customer.orders.sort.TotalAmountDesc': 'Nejdražší',
  'customer.orders.sort.TotalAmountAsc': 'Nejlevnější',
  'customer.orders.sort.StateAsc': 'Podle stavu',

  'customer.orders.table.number': 'Číslo',
  'customer.orders.table.state': 'Stav',
  'customer.orders.table.maker': 'Výrobce',
  'customer.orders.table.product': 'Výrobek',
  'customer.orders.table.total': 'Celkem',
  'customer.orders.table.created': 'Vytvořeno',
  'customer.orders.table.unread': 'Zprávy',
  'customer.orders.unreadAria': 'Nepřečtené zprávy: {count}',

  'customer.orders.empty.title': 'Zatím nemáte žádné objednávky',
  'customer.orders.empty.description': 'Vyberte si výrobce v katalogu a zadejte svou první zakázku.',
  'customer.orders.empty.cta': 'Prohlédnout katalog',
  'customer.orders.noMatch.title': 'Žádné objednávky neodpovídají filtru',
  'customer.orders.noMatch.description': 'Zkuste rozšířit kritéria nebo filtry vymazat.',
  'customer.orders.noMatch.clear': 'Vymazat filtry',

  'customer.orders.error.title': 'Objednávky se nepodařilo načíst',
  'customer.orders.error.retry': 'Zkusit znovu',

  'customer.orders.pagination.previous': 'Předchozí',
  'customer.orders.pagination.next': 'Další',
  'customer.orders.pagination.page_of': 'Stránka {page} z {total}',

  // Customer order tracking detail at /objednavka/[id], Paid+ states
  // (T-0086b, US-customer-0012/0013/0014/0017). Vykání throughout.
  'customer.orderDetail.makerLine': 'Výrobce: {name}',
  'customer.orderDetail.productLine': 'Výrobek: {title}',

  'customer.orderDetail.timeline.heading': 'Průběh objednávky',
  'customer.orderDetail.timeline.created': 'Vytvořeno',
  'customer.orderDetail.timeline.paid': 'Zaplaceno',
  'customer.orderDetail.timeline.accepted': 'Přijato',
  'customer.orderDetail.timeline.shipped': 'Odesláno',
  'customer.orderDetail.timeline.delivered': 'Doručeno',
  'customer.orderDetail.timeline.cancelled': 'Zrušeno',

  'customer.orderDetail.shipping.heading': 'Doprava',
  'customer.orderDetail.shipping.trackingLink': 'Sledovat zásilku',

  'customer.orderDetail.attachments.heading': 'Přílohy',
  'customer.orderDetail.attachments.download': 'Stáhnout',

  'customer.orderDetail.invoice.heading': 'Faktura',
  'customer.orderDetail.invoice.download': 'Stáhnout fakturu',

  // Caption key reserved by T-0076 (supersedes the US draft wording
  // "Potvrdit doručení" — final copy belongs to l10n).
  'customer.orders.markDeliveredButton': 'Označit jako doručeno',
  'customer.orderDetail.markDelivered.inFlight': 'Potvrzuji…',

  // T-0115 customer review-submission surface (vykání). The form + the
  // read-only submitted-review block on /objednavka/[id]. PM/UX to refine.
  'customer.review.heading': 'Vaše hodnocení',
  'customer.review.prompt': 'Jak jste byli s objednávkou spokojeni? Ohodnoťte výrobce.',
  'customer.review.starLabel': '{rating} z 5 hvězdiček',
  'customer.review.ratingGroupLabel': 'Hodnocení hvězdičkami',
  'customer.review.comment.label': 'Komentář (nepovinný)',
  'customer.review.comment.placeholder': 'Napište pár slov o své zkušenosti…',
  'customer.review.comment.counter': '{count}/1000',
  'customer.review.submit': 'Odeslat hodnocení',
  'customer.review.submitting': 'Odesílám…',
  'customer.review.submittedHeading': 'Vaše hodnocení',
  'customer.review.submittedOn': 'Hodnoceno {date}',
  'customer.review.makerReplyHeading': 'Odpověď výrobce',

  // Shared order-message thread (T-0086b creates, T-0087b reuses on the
  // maker detail). Audience-neutral phrasing; {count} keys follow the
  // plural-neutral "Label: N" convention.
  'orderMessages.heading': 'Zprávy',
  'orderMessages.empty': 'Zatím žádné zprávy.',
  'orderMessages.postLabel': 'Nová zpráva',
  'orderMessages.postPlaceholder': 'Napište zprávu…',
  'orderMessages.counter': 'Znaků: {count}/{max}',
  'orderMessages.send': 'Odeslat',
  'orderMessages.sending': 'Odesílám…',
  'orderMessages.loadOlder': 'Načíst starší zprávy',
  'orderMessages.loadingOlder': 'Načítám…',
  'orderMessages.pendingPaymentNote':
    'Zprávy bude možné odesílat po zaplacení objednávky.',

  // Payment confirmation page at /objednavka/[id]/potvrzeni (T-0085).
  // Vykání throughout. Success is granted ONLY by the backend-read Paid
  // state — never by the Comgate redirect params (CLAUDE.md payments rule).
  'checkout.confirm.metadata.title': 'Potvrzení platby — Makables',
  'checkout.confirm.verifying.title': 'Děkujeme! Ověřujeme platbu…',
  'checkout.confirm.verifying.subtitle':
    'Potvrzení od platební brány obvykle dorazí během několika sekund. Stránku prosím nezavírejte.',
  'checkout.confirm.success.title': 'Platba proběhla úspěšně',
  'checkout.confirm.success.orderNumber': 'Objednávka {orderNumber}',
  'checkout.confirm.success.whatNext': 'Co bude dál',
  'checkout.confirm.success.step1': 'Výrobce vaši objednávku přijme.',
  'checkout.confirm.success.step2': 'Vyrobí ji a odešle.',
  'checkout.confirm.success.step3': 'Po převzetí potvrdíte doručení.',
  'checkout.confirm.success.detailCta': 'Zobrazit objednávku',
  'checkout.confirm.success.catalogCta': 'Zpět do katalogu',
  'checkout.confirm.pendingTitle': 'Platbu stále ověřujeme',
  'checkout.confirm.pendingEmailNote':
    'Potvrzení vám pošleme e-mailem, jakmile platbu ověříme.',
  'checkout.confirm.pendingDetailLink': 'Přejít na objednávku',
  'checkout.confirm.failed.title': 'Platba nebyla dokončena',
  'checkout.confirm.failed.heldNote':
    'Objednávku pro vás držíme 24 hodin — platbu můžete dokončit z detailu objednávky.',
  'checkout.confirm.failed.retryCta': 'Dokončit platbu',

  // Maker dashboard order list at /dashboard/maker/objednavky (T-0087a,
  // US-maker-0005). Tykání throughout per CLAUDE.md — PENDING the tone
  // question in docs/questions/open.md; a flip to vykání is a catalog-
  // only change. {count} keys follow the plural-neutral "Label: N"
  // convention from the catalog block above.
  'dashboard.maker.orders.title': 'Objednávky',
  'dashboard.maker.orders.subtitle':
    'Přehled tvých objednávek — nové čekající na přijetí, zakázky ve výrobě i kompletní historie.',
  'dashboard.maker.orders.metadata.title': 'Objednávky — Makables',
  'dashboard.maker.orders.metadata.description':
    'Správa objednávek tvé dílny na Makables.',
  'dashboard.maker.orders.count': 'Objednávek: {count}',
  'dashboard.maker.orders.customOrder': 'Vlastní zakázka',

  'dashboard.maker.orders.tab.nove': 'Nové',
  'dashboard.maker.orders.tab.vyroba': 'Ve výrobě',
  'dashboard.maker.orders.tab.vse': 'Vše',

  'dashboard.maker.orders.filter.dateFrom': 'Od data',
  'dashboard.maker.orders.filter.dateTo': 'Do data',
  'dashboard.maker.orders.filter.sort': 'Řazení',
  'dashboard.maker.orders.filter.reset': 'Vymazat filtry',

  'dashboard.maker.orders.sort.CreatedAtDesc': 'Nejnovější',
  'dashboard.maker.orders.sort.CreatedAtAsc': 'Nejstarší',
  'dashboard.maker.orders.sort.TotalAmountDesc': 'Nejdražší',
  'dashboard.maker.orders.sort.TotalAmountAsc': 'Nejlevnější',
  'dashboard.maker.orders.sort.StateAsc': 'Podle stavu',

  'dashboard.maker.orders.table.number': 'Číslo',
  'dashboard.maker.orders.table.state': 'Stav',
  'dashboard.maker.orders.table.customer': 'Zákazník',
  'dashboard.maker.orders.table.product': 'Výrobek',
  'dashboard.maker.orders.table.payout': 'Tvoje odměna',
  'dashboard.maker.orders.table.created': 'Vytvořeno',
  'dashboard.maker.orders.table.unread': 'Zprávy',
  'dashboard.maker.orders.unreadAria': 'Nepřečtené zprávy: {count}',

  // Per-tab empty states (AC-8): Nové is informational/positive — no
  // new work waiting is a GOOD state, not an error; Vše is onboarding-
  // flavoured for makers without a single order yet.
  'dashboard.maker.orders.empty.nove.title': 'Žádné nové objednávky nečekají',
  'dashboard.maker.orders.empty.nove.description':
    'Vše je vyřízené. Jakmile zákazník zaplatí novou objednávku, objeví se tady.',
  'dashboard.maker.orders.empty.vyroba.title': 'Nic není ve výrobě',
  'dashboard.maker.orders.empty.vyroba.description':
    'Tady uvidíš objednávky, které jsi přijal a na kterých právě pracuješ.',
  'dashboard.maker.orders.empty.vse.title': 'Zatím nemáš žádné objednávky',
  'dashboard.maker.orders.empty.vse.description':
    'Objednávky se tu objeví, jakmile si zákazníci koupí tvé výrobky.',

  'dashboard.maker.orders.error.title': 'Objednávky se nepodařilo načíst',
  'dashboard.maker.orders.error.body': 'Zkus prosím obnovit stránku za chvíli.',
  'dashboard.maker.orders.error.retry': 'Zkusit znovu',

  'dashboard.maker.orders.pagination.previous': 'Předchozí',
  'dashboard.maker.orders.pagination.next': 'Další',
  'dashboard.maker.orders.pagination.page_of': 'Stránka {page} z {total}',

  // Maker payouts dashboard at /dashboard/maker/vyplaty (T-0116,
  // US-maker-0012/0013). Tykání throughout per CLAUDE.md. Two-value state
  // mapping (no Pending). NO CSV anywhere — the maker downloads only their
  // own Fee-invoice PDF.
  'dashboard.maker.nav.payouts': 'Výplaty',
  'dashboard.maker.payouts.metadata.title': 'Výplaty — Makables',
  'dashboard.maker.payouts.metadata.description':
    'Přehled tvých výplat a faktur za provizi na Makables.',
  'dashboard.maker.payouts.title': 'Výplaty',
  'dashboard.maker.payouts.subtitle':
    'Přehled tvých výplatních dávek — co ti bylo vyplaceno za dokončené objednávky.',
  'dashboard.maker.payouts.count': 'Výplat: {count}',
  'dashboard.maker.payouts.state.processing': 'Připravujeme',
  'dashboard.maker.payouts.state.completed': 'Vyplaceno',
  'dashboard.maker.payouts.orderCount': 'Objednávek: {count}',
  'dashboard.maker.payouts.table.number': 'Dávka',
  'dashboard.maker.payouts.table.total': 'Vyplaceno',
  'dashboard.maker.payouts.table.orders': 'Objednávky',
  'dashboard.maker.payouts.table.state': 'Stav',
  'dashboard.maker.payouts.table.date': 'Datum',
  'dashboard.maker.payouts.datePlaceholder': '—',
  'dashboard.maker.payouts.empty.title': 'Zatím nemáš žádné výplaty',
  'dashboard.maker.payouts.empty.description':
    'Jakmile budou tvé objednávky dokončené a proběhne výplatní dávka, najdeš ji tady.',
  'dashboard.maker.payouts.error.title': 'Výplaty se nepodařilo načíst',
  'dashboard.maker.payouts.error.body':
    'Zkus to prosím znovu. Pokud potíže přetrvávají, ozvi se nám.',
  'dashboard.maker.payouts.error.retry': 'Zkusit znovu',
  'dashboard.maker.payouts.pagination.previous': 'Předchozí',
  'dashboard.maker.payouts.pagination.next': 'Další',
  'dashboard.maker.payouts.pagination.page_of': 'Stránka {page} z {total}',

  // T-0117 maker review-reply dashboard (tykání — maker surface; a tone
  // flip is catalog-only per the open question). PM/UX to refine.
  'dashboard.maker.nav.reviews': 'Recenze',
  'dashboard.maker.reviews.metadata.title': 'Recenze — Makables',
  'dashboard.maker.reviews.metadata.description': 'Recenze zákazníků k tvým objednávkám na Makables.',
  'dashboard.maker.reviews.title': 'Recenze',
  'dashboard.maker.reviews.subtitle': 'Přečti si, co zákazníci napsali, a odpověz jim veřejně.',
  'dashboard.maker.reviews.aggregate.label': 'Průměrné hodnocení',
  'dashboard.maker.reviews.aggregate.count': 'Počet recenzí: {count}',
  'dashboard.maker.reviews.aggregate.none': 'Zatím bez hodnocení',
  'dashboard.maker.reviews.card.order': 'Objednávka {orderNumber}',
  'dashboard.maker.reviews.card.noComment': 'Bez komentáře',
  'dashboard.maker.reviews.reply.heading': 'Tvoje odpověď',
  'dashboard.maker.reviews.reply.answeredOn': 'Odpovězeno {date}',
  'dashboard.maker.reviews.reply.edit': 'Upravit odpověď',
  'dashboard.maker.reviews.reply.label': 'Veřejná odpověď',
  'dashboard.maker.reviews.reply.placeholder': 'Poděkuj zákazníkovi nebo reaguj na jeho zpětnou vazbu…',
  'dashboard.maker.reviews.reply.hint': 'Maximálně 500 znaků',
  'dashboard.maker.reviews.reply.submit': 'Odeslat odpověď',
  'dashboard.maker.reviews.reply.submitting': 'Odesílám…',
  'dashboard.maker.reviews.reply.cancel': 'Zrušit',
  'dashboard.maker.reviews.empty.title': 'Zatím nemáš žádné recenze',
  'dashboard.maker.reviews.empty.description':
    'Jakmile zákazníci ohodnotí své doručené objednávky, jejich recenze se zobrazí tady.',
  'dashboard.maker.reviews.error.title': 'Recenze se nepodařilo načíst',
  'dashboard.maker.reviews.error.body': 'Zkus to prosím za chvíli znovu.',
  'dashboard.maker.reviews.error.retry': 'Zkusit znovu',
  'dashboard.maker.reviews.pagination.previous': 'Předchozí',
  'dashboard.maker.reviews.pagination.next': 'Další',
  'dashboard.maker.reviews.pagination.page_of': 'Stránka {page} z {total}',
  'dashboard.maker.payoutDetail.metadata.title': 'Detail výplaty — Makables',
  'dashboard.maker.payoutDetail.metadata.description': 'Rozpis výplatní dávky na Makables.',
  'dashboard.maker.payoutDetail.backToList': 'Zpět na výplaty',
  'dashboard.maker.payoutDetail.summary.heading': 'Souhrn výplaty',
  'dashboard.maker.payoutDetail.summary.number': 'Číslo dávky',
  'dashboard.maker.payoutDetail.summary.total': 'Vyplaceno celkem',
  'dashboard.maker.payoutDetail.summary.orders': 'Počet objednávek',
  'dashboard.maker.payoutDetail.summary.state': 'Stav',
  'dashboard.maker.payoutDetail.summary.date': 'Datum',
  'dashboard.maker.payoutDetail.breakdown.heading': 'Rozpis objednávek',
  'dashboard.maker.payoutDetail.breakdown.order': 'Objednávka',
  'dashboard.maker.payoutDetail.breakdown.productPrice': 'Cena výrobku',
  'dashboard.maker.payoutDetail.breakdown.shipping': 'Doprava',
  'dashboard.maker.payoutDetail.breakdown.platformFee': 'Provize platformy',
  'dashboard.maker.payoutDetail.breakdown.netPayout': 'Čistá výplata',
  'dashboard.maker.payoutDetail.download.label': 'Stáhnout fakturu',
  'dashboard.maker.payoutDetail.download.pending': 'Stahuji…',
  'dashboard.maker.payoutDetail.download.error':
    'Fakturu se nepodařilo stáhnout. Zkus to prosím znovu.',
  'dashboard.maker.payoutDetail.notFound.title': 'Výplata nebyla nalezena',
  'dashboard.maker.payoutDetail.notFound.body':
    'Tato výplatní dávka neexistuje nebo k ní nemáš přístup.',

  // Maker order detail at /dashboard/maker/objednavky/[orderId]
  // (T-0087b, US-maker-0006..0011). Tykání throughout per CLAUDE.md —
  // PENDING the tone question in docs/questions/open.md (catalog-only
  // flip). Error-code parity keys `order.notFound`,
  // `order.invalidTransition`, `shipping.methodNotEligible` and
  // `shipping.carrierUnavailable` already exist above; audience-neutral
  // keys (`order.state.*`, `order.page.title`,
  // `order.page.shippingMethod.*`, `orderMessages.*`) are reused.
  'dashboard.maker.orderDetail.metadata.title': 'Objednávka — Makables',
  'dashboard.maker.orderDetail.backToList': 'Zpět na objednávky',
  'dashboard.maker.orderDetail.createdLine': 'Vytvořeno {date}',
  'dashboard.maker.orderDetail.productLine': 'Výrobek: {title}',

  'dashboard.maker.orderDetail.notFound.title': 'Objednávka nenalezena',
  'dashboard.maker.orderDetail.notFound.body':
    'Tato objednávka neexistuje nebo k ní nemáš přístup.',
  'dashboard.maker.orderDetail.loadError.title': 'Objednávku se nepodařilo načíst',
  'dashboard.maker.orderDetail.loadError.body': 'Zkus prosím obnovit stránku za chvíli.',
  'dashboard.maker.orderDetail.loadError.retry': 'Zkusit znovu',

  // Payout-prominent breakdown (T-0081/T-0082 lock: the maker's payout
  // is the headline figure; no platform-fee row exists on the DTO).
  'dashboard.maker.orderDetail.payout.heading': 'Tvoje odměna',
  'dashboard.maker.orderDetail.payout.total': 'Zákazník zaplatil celkem',
  'dashboard.maker.orderDetail.payout.product': 'Výrobek',
  'dashboard.maker.orderDetail.payout.shipping': 'Doprava',
  'dashboard.maker.orderDetail.payout.vat': 'DPH {rate} %',

  'dashboard.maker.orderDetail.timeline.heading': 'Průběh objednávky',
  'dashboard.maker.orderDetail.timeline.created': 'Vytvořeno',
  'dashboard.maker.orderDetail.timeline.paid': 'Zaplaceno',
  'dashboard.maker.orderDetail.timeline.accepted': 'Přijato',
  'dashboard.maker.orderDetail.timeline.shipped': 'Odesláno',
  'dashboard.maker.orderDetail.timeline.delivered': 'Doručeno',
  'dashboard.maker.orderDetail.timeline.cancelled': 'Zrušeno',

  'dashboard.maker.orderDetail.shipping.heading': 'Doprava',
  'dashboard.maker.orderDetail.shipping.pickupPoint': 'Výdejní místo Zásilkovny: {id}',
  'dashboard.maker.orderDetail.shipping.trackingLink': 'Sledovat zásilku',

  'dashboard.maker.orderDetail.contact.heading': 'Kontakt na zákazníka',

  'dashboard.maker.orderDetail.attachments.heading': 'Přílohy od zákazníka',
  'dashboard.maker.orderDetail.attachments.download': 'Stáhnout',

  'dashboard.maker.orderDetail.invoice.heading': 'Faktura',
  'dashboard.maker.orderDetail.invoice.download': 'Stáhnout fakturu',

  'dashboard.maker.orderDetail.action.accept': 'Přijmout objednávku',
  'dashboard.maker.orderDetail.action.accepting': 'Přijímám…',
  'dashboard.maker.orderDetail.action.ship': 'Odeslat',
  'dashboard.maker.orderDetail.action.shipping': 'Odesílám…',
  'dashboard.maker.orderDetail.action.handover': 'Předat osobně',
  'dashboard.maker.orderDetail.action.handingOver': 'Předávám…',
  'dashboard.maker.orderDetail.action.downloadLabel': 'Stáhnout štítek',
  'dashboard.maker.orderDetail.action.downloadingLabel': 'Stahuji štítek…',

  // Ship-only confirm dialog (T-0087b §C: the carrier shipment + label
  // are real-world, irreversible side effects — T-0072).
  'dashboard.maker.orderDetail.shipConfirm.title': 'Odeslat zásilku přes Zásilkovnu?',
  'dashboard.maker.orderDetail.shipConfirm.body':
    'Potvrzením vytvoříme skutečnou zásilku u dopravce a vygenerujeme štítek. Tento krok je nevratný — pokračuj, jen pokud je balíček připravený k odeslání.',
  'dashboard.maker.orderDetail.shipConfirm.confirm': 'Ano, odeslat',
  'dashboard.maker.orderDetail.shipConfirm.cancel': 'Zrušit',

  // ===========================================================================
  // Admin dashboard (T-0118a) — vykání (V form: admins are JVM YORE operators,
  // addressed formally). Every admin-facing string is keyed here (T8 gate live).
  // ===========================================================================

  // Admin shell — nav + header
  'dashboard.admin.shell.brand': 'Makables Admin',
  'dashboard.admin.shell.identityFallback': 'Administrátor',
  'dashboard.admin.shell.logout': 'Odhlásit se',
  'dashboard.admin.shell.openMenu': 'Otevřít menu',
  'dashboard.admin.nav.overview': 'Přehled',
  'dashboard.admin.nav.orders': 'Objednávky',
  'dashboard.admin.nav.invoices': 'Faktury',
  'dashboard.admin.nav.audit': 'Audit log',
  // Forward-compat nav entries (slices b/c) — rendered visibly pending,
  // never as live links to not-yet-built routes (AC-3 / Option H).
  'dashboard.admin.nav.payouts': 'Výplaty',
  'dashboard.admin.nav.outbox': 'Fronta událostí',
  'dashboard.admin.nav.makers': 'Makeři',
  'dashboard.admin.nav.users': 'Uživatelé',
  'dashboard.admin.nav.config': 'Nastavení zemí',
  'dashboard.admin.nav.pendingBadge': 'Připravujeme',

  // Admin login
  'dashboard.admin.login.metadata.title': 'Přihlášení administrátora — Makables',
  'dashboard.admin.login.title': 'Přihlášení administrátora',
  'dashboard.admin.login.subtitle': 'Přístup pouze pro provozovatele platformy.',
  'dashboard.admin.login.email': 'E-mail',
  'dashboard.admin.login.password': 'Heslo',
  'dashboard.admin.login.submit': 'Přihlásit se',
  'dashboard.admin.login.submitting': 'Přihlašuji…',
  'dashboard.admin.login.error.invalidCredentials': 'Nesprávný e-mail nebo heslo.',
  'dashboard.admin.login.error.locked':
    'Účet je dočasně uzamčen kvůli opakovaným neúspěšným pokusům. Zkuste to prosím za chvíli.',
  'dashboard.admin.login.error.forbidden': 'Tento účet nemá oprávnění administrátora.',
  'dashboard.admin.login.error.oauthNotAllowed':
    'Administrátoři se přihlašují pouze e-mailem a heslem.',
  'dashboard.admin.login.error.generic': 'Přihlášení se nezdařilo. Zkuste to prosím znovu.',

  // Overview
  'dashboard.admin.overview.metadata.title': 'Přehled — Makables Admin',
  'dashboard.admin.overview.metadata.description':
    'Přehled stavu platformy: objednávky, výplaty, fronta událostí a spory.',
  'dashboard.admin.overview.title': 'Přehled platformy',
  'dashboard.admin.overview.subtitle': 'Rychlá orientace ve stavu objednávek, výplat a provozu.',
  'dashboard.admin.overview.orders.heading': 'Objednávky podle stavu',
  'dashboard.admin.overview.ops.heading': 'Provoz',
  'dashboard.admin.overview.tile.viewList': 'Zobrazit seznam',
  'dashboard.admin.overview.tile.unavailableAria': 'Počet není k dispozici',
  'dashboard.admin.overview.tile.paid': 'Zaplacené',
  'dashboard.admin.overview.tile.accepted': 'Přijaté',
  'dashboard.admin.overview.tile.shipped': 'Odeslané',
  'dashboard.admin.overview.tile.disputed': 'Spory',
  'dashboard.admin.overview.tile.payouts': 'Výplaty ke zpracování',
  'dashboard.admin.overview.tile.outbox': 'Zaseknuté události',
  'dashboard.admin.overview.tile.pendingNote':
    'Tato sekce se připravuje — odkaz vede na budoucí přehled.',
  'dashboard.admin.overview.countFollowUp':
    'Souhrnné počty zatím nejsou k dispozici. Otevřete seznam pro aktuální data.',
  'dashboard.admin.overview.error.title': 'Přehled se nepodařilo načíst',
  'dashboard.admin.overview.error.retry': 'Zkusit znovu',

  // All-orders list
  'dashboard.admin.orders.metadata.title': 'Objednávky — Makables Admin',
  'dashboard.admin.orders.metadata.description': 'Všechny objednávky napříč makery a zákazníky.',
  'dashboard.admin.orders.title': 'Objednávky',
  'dashboard.admin.orders.subtitle': 'Všechny objednávky platformy, seřazené od nejnovějších.',
  'dashboard.admin.orders.count': 'Celkem {count} objednávek',
  'dashboard.admin.orders.table.number': 'Číslo',
  'dashboard.admin.orders.table.created': 'Vytvořeno',
  'dashboard.admin.orders.table.state': 'Stav',
  'dashboard.admin.orders.table.maker': 'Maker',
  'dashboard.admin.orders.table.customer': 'Zákazník',
  'dashboard.admin.orders.table.country': 'Země',
  'dashboard.admin.orders.table.total': 'Částka',
  'dashboard.admin.orders.filter.state': 'Stav',
  'dashboard.admin.orders.filter.stateAll': 'Všechny stavy',
  'dashboard.admin.orders.filter.country': 'Země',
  'dashboard.admin.orders.filter.maker': 'ID makera',
  'dashboard.admin.orders.filter.customer': 'E-mail zákazníka',
  'dashboard.admin.orders.filter.apply': 'Filtrovat',
  'dashboard.admin.orders.filter.reset': 'Vymazat filtry',
  'dashboard.admin.orders.empty.title': 'Žádné objednávky',
  'dashboard.admin.orders.empty.description':
    'Zadaným filtrům neodpovídají žádné objednávky. Upravte filtry nebo je vymažte.',
  'dashboard.admin.orders.error.title': 'Objednávky se nepodařilo načíst',
  'dashboard.admin.orders.error.body':
    'Při načítání seznamu objednávek došlo k chybě. Zkuste to prosím znovu.',
  'dashboard.admin.orders.error.retry': 'Zkusit znovu',
  'dashboard.admin.orders.pagination.previous': 'Předchozí',
  'dashboard.admin.orders.pagination.next': 'Další',
  'dashboard.admin.orders.pagination.page_of': 'Stránka {page} z {total}',

  // All-invoices list
  'dashboard.admin.invoices.metadata.title': 'Faktury — Makables Admin',
  'dashboard.admin.invoices.metadata.description': 'Všechny faktury platformy.',
  'dashboard.admin.invoices.title': 'Faktury',
  'dashboard.admin.invoices.subtitle': 'Všechny faktury platformy, seřazené od nejnovějších.',
  'dashboard.admin.invoices.count': 'Celkem {count} faktur',
  'dashboard.admin.invoices.table.number': 'Číslo',
  'dashboard.admin.invoices.table.type': 'Typ',
  'dashboard.admin.invoices.table.country': 'Země',
  'dashboard.admin.invoices.table.recipient': 'Příjemce',
  'dashboard.admin.invoices.table.total': 'Částka',
  'dashboard.admin.invoices.table.created': 'Vystaveno',
  'dashboard.admin.invoices.table.actions': 'Akce',
  'dashboard.admin.invoices.type.customer': 'Zákaznická',
  'dashboard.admin.invoices.type.fee': 'Provize',
  'dashboard.admin.invoices.type.unknown': 'Neznámý typ',
  'dashboard.admin.invoices.filter.type': 'Typ',
  'dashboard.admin.invoices.filter.typeAll': 'Všechny typy',
  'dashboard.admin.invoices.filter.country': 'Země',
  'dashboard.admin.invoices.filter.recipient': 'Příjemce',
  'dashboard.admin.invoices.filter.dateFrom': 'Datum od',
  'dashboard.admin.invoices.filter.dateTo': 'Datum do',
  'dashboard.admin.invoices.filter.apply': 'Filtrovat',
  'dashboard.admin.invoices.filter.reset': 'Vymazat filtry',
  'dashboard.admin.invoices.download.label': 'Stáhnout fakturu',
  'dashboard.admin.invoices.download.unavailable':
    'Stahování faktur zatím není dostupné — připravuje se backendový endpoint.',
  // T-0118c: the admin invoice-PDF endpoint (T-0126) now exists — the
  // disabled button is re-enabled as a blob download.
  'dashboard.admin.invoices.download.downloading': 'Stahuji…',
  'dashboard.admin.invoices.download.error':
    'Fakturu se nepodařilo stáhnout. Zkuste to prosím znovu.',
  'dashboard.admin.invoices.empty.title': 'Žádné faktury',
  'dashboard.admin.invoices.empty.description':
    'Zadaným filtrům neodpovídají žádné faktury. Upravte filtry nebo je vymažte.',
  'dashboard.admin.invoices.error.title': 'Faktury se nepodařilo načíst',
  'dashboard.admin.invoices.error.body':
    'Při načítání seznamu faktur došlo k chybě. Zkuste to prosím znovu.',
  'dashboard.admin.invoices.error.retry': 'Zkusit znovu',
  'dashboard.admin.invoices.pagination.previous': 'Předchozí',
  'dashboard.admin.invoices.pagination.next': 'Další',
  'dashboard.admin.invoices.pagination.page_of': 'Stránka {page} z {total}',

  // Audit-log list
  'dashboard.admin.audit.metadata.title': 'Audit log — Makables Admin',
  'dashboard.admin.audit.metadata.description': 'Záznamy administrátorských akcí.',
  'dashboard.admin.audit.title': 'Audit log',
  'dashboard.admin.audit.subtitle': 'Záznamy administrátorských akcí, seřazené od nejnovějších.',
  'dashboard.admin.audit.count': 'Celkem {count} záznamů',
  'dashboard.admin.audit.table.created': 'Čas',
  'dashboard.admin.audit.table.adminUser': 'Administrátor',
  'dashboard.admin.audit.table.action': 'Akce',
  'dashboard.admin.audit.table.target': 'Cíl',
  'dashboard.admin.audit.table.targetId': 'ID cíle',
  'dashboard.admin.audit.table.notes': 'Poznámka',
  'dashboard.admin.audit.notesPlaceholder': '—',
  'dashboard.admin.audit.filter.adminUser': 'ID administrátora',
  'dashboard.admin.audit.filter.action': 'Kód akce',
  'dashboard.admin.audit.filter.target': 'Cílová entita',
  'dashboard.admin.audit.filter.dateFrom': 'Datum od',
  'dashboard.admin.audit.filter.dateTo': 'Datum do',
  'dashboard.admin.audit.filter.apply': 'Filtrovat',
  'dashboard.admin.audit.filter.reset': 'Vymazat filtry',
  'dashboard.admin.audit.empty.title': 'Žádné záznamy',
  'dashboard.admin.audit.empty.description':
    'Zadaným filtrům neodpovídají žádné záznamy. Upravte filtry nebo je vymažte.',
  'dashboard.admin.audit.error.title': 'Audit log se nepodařilo načíst',
  'dashboard.admin.audit.error.body':
    'Při načítání audit logu došlo k chybě. Zkuste to prosím znovu.',
  'dashboard.admin.audit.error.retry': 'Zkusit znovu',
  'dashboard.admin.audit.pagination.previous': 'Předchozí',
  'dashboard.admin.audit.pagination.next': 'Další',
  'dashboard.admin.audit.pagination.page_of': 'Stránka {page} z {total}',

  // T-0118b admin order detail + money/state action surfaces (vykání).
  // The surfaced error codes (payment.refund.*, order.manualTransition.*,
  // order.dispute.*, order.invalidTransition) already have keys above —
  // resolveErrorMessage maps them 1:1. PM/UX to refine copy on PR review.
  'dashboard.admin.orderActions.metadata.title': 'Detail objednávky — Makables Admin',
  'dashboard.admin.orderActions.metadata.description':
    'Detail objednávky, historie auditu a administrátorské akce.',
  'dashboard.admin.orderActions.backToList': 'Zpět na objednávky',
  'dashboard.admin.orderActions.section.actions': 'Akce',
  'dashboard.admin.orderActions.section.dispute': 'Reklamace',

  'dashboard.admin.orderActions.header.idLabel': 'ID objednávky',
  'dashboard.admin.orderActions.header.total': 'Částka',
  'dashboard.admin.orderActions.header.country': 'Země',
  'dashboard.admin.orderActions.header.maker': 'Maker',
  'dashboard.admin.orderActions.header.customer': 'E-mail zákazníka',
  'dashboard.admin.orderActions.header.degraded.title': 'Detail objednávky není k dispozici',
  'dashboard.admin.orderActions.header.degraded.body':
    'Hlavičku objednávky se nepodařilo načíst, proto nejsou dostupné akce. Historie auditu níže zůstává platná. Zkuste stránku obnovit.',

  'dashboard.admin.orderActions.notFound.title': 'Objednávka nenalezena',
  'dashboard.admin.orderActions.notFound.body':
    'Tato objednávka neexistuje nebo k ní nemáme žádný záznam.',

  // Refund modal (A.1) — money path, post-payout ack.
  'dashboard.admin.orderActions.refund.trigger': 'Refundovat',
  'dashboard.admin.orderActions.refund.title': 'Refundace objednávky {orderNumber}',
  'dashboard.admin.orderActions.refund.intro':
    'Zadejte částku k refundaci a důvod. Plnou částku můžete snížit pro částečnou refundaci.',
  'dashboard.admin.orderActions.refund.irreversibleNote':
    'Refundace přesouvá skutečné peníze přes platební bránu a nelze ji z administrace vrátit zpět.',
  'dashboard.admin.orderActions.refund.amountLabel': 'Částka k refundaci (Kč)',
  'dashboard.admin.orderActions.refund.amountHint': 'Celková částka objednávky: {total}.',
  'dashboard.admin.orderActions.refund.reasonLabel': 'Důvod refundace',
  'dashboard.admin.orderActions.refund.postPayoutAck':
    'Objednávka už byla vyplacena výrobci. Potvrzuji, že přesto chci provést refundaci.',
  'dashboard.admin.orderActions.refund.submit': 'Refundovat',
  'dashboard.admin.orderActions.refund.submitting': 'Refunduji…',

  // Manual state change modal (A.2) — mandatory reason, backend allow-list.
  'dashboard.admin.orderActions.state.trigger': 'Změnit stav',
  'dashboard.admin.orderActions.state.title': 'Ruční změna stavu objednávky {orderNumber}',
  'dashboard.admin.orderActions.state.intro':
    'Aktuální stav: {current}. Vyberte cílový stav. Povolené přechody vyhodnotí systém.',
  'dashboard.admin.orderActions.state.targetLabel': 'Cílový stav',
  'dashboard.admin.orderActions.state.reasonLabel': 'Důvod změny',
  'dashboard.admin.orderActions.state.reasonHint': 'Důvod musí mít alespoň 10 znaků.',
  'dashboard.admin.orderActions.state.submit': 'Změnit stav',
  'dashboard.admin.orderActions.state.submitting': 'Měním stav…',

  // Dispute inline forms (A.3) — open + resolve.
  'dashboard.admin.orderActions.dispute.categoryLabel': 'Kategorie reklamace',
  'dashboard.admin.orderActions.dispute.descriptionLabel': 'Popis reklamace',
  'dashboard.admin.orderActions.dispute.outcomeLabel': 'Výsledek reklamace',
  'dashboard.admin.orderActions.dispute.resolutionNotesLabel': 'Poznámka k vyřízení',
  'dashboard.admin.orderActions.dispute.resolutionNotesHint':
    'Tato poznámka je viditelná pro zákazníka.',
  'dashboard.admin.orderActions.dispute.open.title': 'Otevřít reklamaci',
  'dashboard.admin.orderActions.dispute.open.intro':
    'Zaznamenejte reklamaci nahlášenou zákazníkem (např. telefonicky).',
  'dashboard.admin.orderActions.dispute.open.submit': 'Otevřít reklamaci',
  'dashboard.admin.orderActions.dispute.open.submitting': 'Otevírám…',
  'dashboard.admin.orderActions.dispute.resolve.title': 'Vyřídit reklamaci',
  'dashboard.admin.orderActions.dispute.resolve.intro':
    'Po prostudování historie auditu vyberte výsledek a doplňte poznámku.',
  'dashboard.admin.orderActions.dispute.resolve.submit': 'Vyřídit reklamaci',
  'dashboard.admin.orderActions.dispute.resolve.submitting': 'Vyřizuji…',
  'dashboard.admin.orderActions.dispute.category.notDelivered': 'Nedoručeno',
  'dashboard.admin.orderActions.dispute.category.damagedItem': 'Poškozené zboží',
  'dashboard.admin.orderActions.dispute.category.notAsDescribed': 'Neodpovídá popisu',
  'dashboard.admin.orderActions.dispute.category.carrierReturned': 'Vráceno dopravcem',
  'dashboard.admin.orderActions.dispute.category.carrierFailed': 'Selhání dopravce',
  'dashboard.admin.orderActions.dispute.category.other': 'Jiné',
  'dashboard.admin.orderActions.dispute.outcome.refunded': 'Refundováno',
  'dashboard.admin.orderActions.dispute.outcome.resumed': 'Obnoveno',
  'dashboard.admin.orderActions.dispute.outcome.cancelled': 'Zrušeno',

  // Audit-trail panel on the detail (load-bearing read — §C.2).
  'dashboard.admin.orderActions.audit.heading': 'Historie auditu',
  'dashboard.admin.orderActions.audit.empty':
    'K této objednávce zatím nejsou žádné záznamy auditu.',
  'dashboard.admin.orderActions.audit.byAdmin': 'Administrátor: {adminUserId}',
  'dashboard.admin.orderActions.audit.previous': 'Předchozí',
  'dashboard.admin.orderActions.audit.next': 'Další',
  'dashboard.admin.orderActions.audit.error.title': 'Historii auditu se nepodařilo načíst',
  'dashboard.admin.orderActions.audit.error.body':
    'Při načítání historie auditu došlo k chybě. Zkuste stránku obnovit.',

  // =====================================================================
  // T-0118c — Admin ops + control-plane (vykání). Outbox triage /
  // country-config / payout view+complete+CSV / GDPR delete-user. Every
  // rendered string keyed (T8 gate live). Backend error codes consumed
  // from their existing parity keys (outbox.* / country.* / payoutBatch.* /
  // user.*) via resolveErrorMessage.
  // =====================================================================

  // --- Outbox triage ---
  'dashboard.admin.ops.outbox.metadata.title': 'Fronta událostí — Makables Admin',
  'dashboard.admin.ops.outbox.metadata.description':
    'Triáž zaseknutých událostí: opakování a potvrzení.',
  'dashboard.admin.ops.outbox.title': 'Fronta událostí',
  'dashboard.admin.ops.outbox.subtitle':
    'Triáž zaseknutých událostí výstupní fronty. Opakujte doručení, nebo událost potvrďte a přestaňte ji sledovat.',
  'dashboard.admin.ops.outbox.stalledCount.label': 'Zaseknuté události',
  'dashboard.admin.ops.outbox.stalledCount.unavailable':
    'Počet zaseknutých událostí se nepodařilo načíst.',
  'dashboard.admin.ops.outbox.stalledCount.none':
    'Žádné zaseknuté události. Fronta je v pořádku.',
  'dashboard.admin.ops.outbox.listGap.title': 'Seznam událostí zatím není k dispozici',
  'dashboard.admin.ops.outbox.listGap.body':
    'Backend zatím nevystavuje seznam jednotlivých zaseknutých událostí — pouze jejich počet. Akce opakování a potvrzení proto cílí na konkrétní ID události. Doplnění seznamového endpointu je evidováno jako následný backendový úkol.',
  'dashboard.admin.ops.outbox.actions.heading': 'Akce nad událostí',
  'dashboard.admin.ops.outbox.actions.intro':
    'Zadejte ID události a zvolte akci. Opakování je jednorázový pokus o nové doručení; potvrzení vyžaduje důvod a událost přestane být sledována.',
  'dashboard.admin.ops.outbox.eventIdLabel': 'ID události',
  'dashboard.admin.ops.outbox.eventIdHint': 'Identifikátor řádku ve frontě událostí (GUID).',
  'dashboard.admin.ops.outbox.retry.button': 'Opakovat doručení',
  'dashboard.admin.ops.outbox.retry.pending': 'Opakuji…',
  'dashboard.admin.ops.outbox.retry.success':
    'Doručení naplánováno znovu (pokus č. {retryCount}).',
  'dashboard.admin.ops.outbox.ack.button': 'Potvrdit a přestat sledovat',
  'dashboard.admin.ops.outbox.ack.pending': 'Potvrzuji…',
  'dashboard.admin.ops.outbox.ack.reasonLabel': 'Důvod potvrzení',
  'dashboard.admin.ops.outbox.ack.reasonHint':
    'Povinné. Poznámka se zapíše do auditního logu (max. 2000 znaků).',
  'dashboard.admin.ops.outbox.ack.success': 'Událost byla potvrzena a přestane být sledována.',

  // --- Country configuration ---
  'dashboard.admin.ops.country.metadata.title': 'Nastavení země — Makables Admin',
  'dashboard.admin.ops.country.metadata.description':
    'Úprava DPH, provize, poskytovatelů a režimu fakturace pro zemi.',
  'dashboard.admin.ops.country.title': 'Nastavení země {code}',
  'dashboard.admin.ops.country.subtitle':
    'Úprava řídicí konfigurace země: sazby DPH, provize platformy, výchozí poskytovatelé a režim fakturace. Změna se projeví u nových objednávek.',
  'dashboard.admin.ops.country.noPrefillNote':
    'Pozor: uložení přepíše CELOU konfiguraci země (úplná náhrada, ne dílčí úprava). Formulář se zatím nepředvyplňuje, proto musíte zadat VŠECHNY hodnoty — sazby DPH, provizi, cenu dopravy i všechny poskytovatele. Prázdné nebo chybně zadané pole tiše přepíše stávající hodnotu pro všechny budoucí objednávky. Před uložením ověřte každé pole. (Předvyplnění z načítacího endpointu je evidováno jako následný backendový úkol.)',
  'dashboard.admin.ops.country.section.tax': 'Daně a provize',
  'dashboard.admin.ops.country.section.providers': 'Výchozí poskytovatelé',
  'dashboard.admin.ops.country.standardVatLabel': 'Standardní sazba DPH (v bazických bodech)',
  'dashboard.admin.ops.country.standardVatHint':
    '2100 b. b. = 21 %. Zadejte v bazických bodech (1 % = 100 b. b.).',
  'dashboard.admin.ops.country.reducedVatLabel': 'Snížená sazba DPH (v bazických bodech)',
  'dashboard.admin.ops.country.reducedVatHint': 'Volitelné. Ponechte prázdné, pokud se nepoužívá.',
  'dashboard.admin.ops.country.platformFeeLabel': 'Provize platformy (v bazických bodech)',
  'dashboard.admin.ops.country.platformFeeHint': '1500 b. b. = 15 %.',
  'dashboard.admin.ops.country.shippingPriceLabel': 'Výchozí cena dopravy (Kč)',
  'dashboard.admin.ops.country.shippingPriceHint': 'Zadejte v celých korunách.',
  'dashboard.admin.ops.country.invoicingModeLabel': 'Režim fakturace',
  'dashboard.admin.ops.country.invoicingMode.none': 'Žádný',
  'dashboard.admin.ops.country.invoicingMode.standardVat': 'Standardní DPH',
  'dashboard.admin.ops.country.invoicingMode.reverseCharge': 'Přenesená daňová povinnost',
  'dashboard.admin.ops.country.invoicingMode.strictFiscalReporting': 'Striktní fiskální reporting',
  'dashboard.admin.ops.country.paymentProviderLabel': 'Poskytovatel plateb',
  'dashboard.admin.ops.country.shippingCarrierLabel': 'Dopravce',
  'dashboard.admin.ops.country.registryLabel': 'Registr firem',
  'dashboard.admin.ops.country.emailProviderLabel': 'Poskytovatel e-mailů',
  'dashboard.admin.ops.country.reasonLabel': 'Důvod změny',
  'dashboard.admin.ops.country.reasonHint': 'Povinné. Zapíše se do auditního logu.',
  'dashboard.admin.ops.country.save': 'Uložit konfiguraci',
  'dashboard.admin.ops.country.saving': 'Ukládám…',
  'dashboard.admin.ops.country.success':
    'Konfigurace země byla uložena.',
  'dashboard.admin.ops.country.providerModal.title': 'Potvrzení změny poskytovatele',
  'dashboard.admin.ops.country.providerModal.intro':
    'Měníte výchozího poskytovatele. Tato změna ovlivní všechny nové objednávky v zemi. Pro potvrzení přesně přepište nový kód poskytovatele.',
  'dashboard.admin.ops.country.providerModal.changedHeading': 'Měněné kódy poskytovatelů:',
  'dashboard.admin.ops.country.providerModal.confirmLabel': 'Přepište nový kód poskytovatele',
  'dashboard.admin.ops.country.providerModal.confirmPlaceholder': 'Zadejte přesně nový kód',
  'dashboard.admin.ops.country.providerModal.confirm': 'Potvrdit a uložit',
  'dashboard.admin.ops.country.providerModal.confirming': 'Ukládám…',
  'dashboard.admin.ops.country.inFlightAdvisory':
    '{count} rozpracovaných objednávek si ponechá stávajícího poskytovatele. Změna se týká pouze nových objednávek.',

  // --- Payout view + complete + CSV ---
  'dashboard.admin.ops.payouts.metadata.title': 'Výplaty — Makables Admin',
  'dashboard.admin.ops.payouts.metadata.description':
    'Přehled výplatních dávek, dokončení a stažení bankovního CSV.',
  'dashboard.admin.ops.payouts.title': 'Výplaty',
  'dashboard.admin.ops.payouts.subtitle':
    'Správa výplatních dávek: dokončení převodu a stažení bankovního souboru. Vytváření dávek zajišťuje týdenní časovač.',
  'dashboard.admin.ops.payouts.processingCount.label': 'Dávky ke zpracování',
  'dashboard.admin.ops.payouts.processingCount.unavailable':
    'Počet zpracovávaných dávek se nepodařilo načíst.',
  'dashboard.admin.ops.payouts.processingCount.none':
    'Žádné dávky ke zpracování.',
  'dashboard.admin.ops.payouts.listGap.title': 'Seznam dávek zatím není k dispozici',
  'dashboard.admin.ops.payouts.listGap.body':
    'Backend zatím nevystavuje seznam výplatních dávek — pouze počet zpracovávaných. Akce dokončení a stažení CSV proto cílí na konkrétní ID dávky. Doplnění seznamového endpointu je evidováno jako následný backendový úkol.',
  'dashboard.admin.ops.payouts.actions.heading': 'Akce nad dávkou',
  'dashboard.admin.ops.payouts.actions.intro':
    'Zadejte ID dávky pro dokončení převodu nebo stažení bankovního CSV souboru.',
  'dashboard.admin.ops.payouts.batchIdLabel': 'ID výplatní dávky',
  'dashboard.admin.ops.payouts.batchIdHint': 'Identifikátor výplatní dávky (GUID).',
  'dashboard.admin.ops.payouts.batchNumberLabel': 'Číslo dávky (pro název souboru)',
  'dashboard.admin.ops.payouts.batchNumberHint':
    'Volitelné. Použije se v názvu staženého CSV (vyplaty-{číslo}.csv).',
  'dashboard.admin.ops.payouts.state.processing': 'Zpracovává se',
  'dashboard.admin.ops.payouts.state.completed': 'Vyplaceno',
  'dashboard.admin.ops.payouts.complete.button': 'Označit jako vyplacené',
  'dashboard.admin.ops.payouts.complete.title': 'Dokončit výplatní dávku',
  'dashboard.admin.ops.payouts.complete.intro':
    'Po provedení bankovního převodu zadejte referenci a (volitelně) datum platby. Tato akce je nevratná — dávka přejde do stavu Vyplaceno.',
  'dashboard.admin.ops.payouts.complete.bankReferenceLabel': 'Bankovní reference',
  'dashboard.admin.ops.payouts.complete.bankReferenceHint': 'Povinné. Reference provedeného převodu.',
  'dashboard.admin.ops.payouts.complete.paymentDateLabel': 'Datum platby',
  'dashboard.admin.ops.payouts.complete.paymentDateHint': 'Volitelné.',
  'dashboard.admin.ops.payouts.complete.submit': 'Dokončit dávku',
  'dashboard.admin.ops.payouts.complete.submitting': 'Dokončuji…',
  'dashboard.admin.ops.payouts.complete.success':
    'Dávka {batchId} byla označena jako vyplacená ({total}).',
  'dashboard.admin.ops.payouts.complete.alreadyCompleted':
    'Tato dávka už byla dříve dokončena — stav se nezměnil.',
  'dashboard.admin.ops.payouts.csv.button': 'Stáhnout CSV',
  'dashboard.admin.ops.payouts.csv.pending': 'Stahuji…',
  'dashboard.admin.ops.payouts.csv.intro':
    'Bankovní soubor pro provedení převodu (obsahuje čísla účtů všech makerů — pouze pro operátora).',
  'dashboard.admin.ops.payouts.csv.error':
    'CSV soubor se nepodařilo stáhnout. Zkuste to prosím znovu.',

  // --- Delete-user (the dangerous screen) ---
  'dashboard.admin.ops.users.metadata.title': 'Smazání uživatele — Makables Admin',
  'dashboard.admin.ops.users.metadata.description':
    'Trvalé smazání uživatele dle GDPR. Nevratná operace.',
  'dashboard.admin.ops.users.title': 'Trvalé smazání uživatele',
  'dashboard.admin.ops.users.subtitle':
    'Trvalé smazání osobních údajů uživatele dle GDPR. Tato operace je nevratná a vyžaduje přesné potvrzení.',
  'dashboard.admin.ops.users.lookup.heading': 'Vyhledání uživatele',
  'dashboard.admin.ops.users.lookup.intro':
    'Zadejte ID uživatele a jeho e-mail. E-mail musíte níže přesně přepsat pro potvrzení smazání.',
  'dashboard.admin.ops.users.lookup.idLabel': 'ID uživatele',
  'dashboard.admin.ops.users.lookup.idHint': 'Identifikátor uživatele (GUID).',
  'dashboard.admin.ops.users.lookup.emailLabel': 'E-mail uživatele',
  'dashboard.admin.ops.users.lookup.emailHint':
    'E-mail uživatele, kterého chcete smazat. Bude vyžadováno jeho přesné přepsání.',
  'dashboard.admin.ops.users.lookup.submit': 'Pokračovat ke smazání',
  'dashboard.admin.ops.users.lookupGap.title': 'Vyhledání uživatele zatím není automatické',
  'dashboard.admin.ops.users.lookupGap.body':
    'Backend zatím nevystavuje vyhledání uživatele ani jeho rozpracovaných objednávek. ID a e-mail proto zadáváte ručně a blokaci kvůli rozpracovaným objednávkám zobrazí backend až při odeslání. Doplnění čtecích endpointů je evidováno jako následný backendový úkol.',
  'dashboard.admin.ops.users.irreversibleBanner.title': 'Nevratné smazání',
  'dashboard.admin.ops.users.irreversibleBanner.body':
    'Data uživatele nelze obnovit. Faktury zůstávají zachovány dle GDPR čl. 17 odst. 3 písm. b).',
  'dashboard.admin.ops.users.targetEmailLabel': 'Uživatel ke smazání',
  'dashboard.admin.ops.users.confirmEmailLabel': 'Pro potvrzení přepište přesně e-mail uživatele',
  'dashboard.admin.ops.users.confirmEmailPlaceholder': 'Zadejte přesně e-mail uživatele',
  'dashboard.admin.ops.users.confirmEmailMismatch':
    'Zadaný e-mail zatím neodpovídá uživateli.',
  'dashboard.admin.ops.users.reasonLabel': 'Důvod smazání',
  'dashboard.admin.ops.users.reasonHint':
    'Povinné. Uveďte referenci GDPR žádosti (zapíše se do auditního logu, max. 2000 znaků).',
  'dashboard.admin.ops.users.inFlightReason':
    'Uživatele nelze smazat — má rozpracované objednávky. Nejprve je vyřešte.',
  'dashboard.admin.ops.users.erase.button': 'Trvale smazat uživatele',
  'dashboard.admin.ops.users.erase.pending': 'Mažu…',
  'dashboard.admin.ops.users.erase.disabledHint':
    'Tlačítko se aktivuje po přepsání e-mailu a vyplnění důvodu.',
  'dashboard.admin.ops.users.erase.success.title': 'Uživatel byl trvale smazán',
  'dashboard.admin.ops.users.erase.success.body':
    'Osobní údaje uživatele {userId} byly nevratně odstraněny. Faktury zůstávají zachovány dle GDPR.',
  'dashboard.admin.ops.users.erase.alreadyDeleted':
    'Uživatel již byl smazán.',
  'dashboard.admin.ops.users.reset': 'Zpět na vyhledání',

  // --- Shared route error/loading copy for the ops segments ---
  'dashboard.admin.ops.error.title': 'Stránku se nepodařilo načíst',
  'dashboard.admin.ops.error.body':
    'Při načítání stránky došlo k chybě. Zkuste ji prosím obnovit.',
  'dashboard.admin.ops.error.retry': 'Zkusit znovu',
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
