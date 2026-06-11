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
