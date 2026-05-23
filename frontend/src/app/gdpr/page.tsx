import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'Ochrana osobních údajů',
  description: 'Zásady ochrany osobních údajů platformy Makables.',
};

export default function GdprPage() {
  return (
    <div className="bg-surface-primary py-20 sm:py-28">
      <div className="mx-auto max-w-3xl px-4 sm:px-6 lg:px-8">
        <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Právní dokumenty</p>
        <h1 className="mt-3 text-3xl font-bold tracking-tight text-white sm:text-4xl">
          Ochrana osobních údajů
        </h1>
        <p className="mt-4 text-sm text-zinc-500">
          Platné od 1. 1. 2026 &middot; Poslední aktualizace: 1. 5. 2026
        </p>

        <div className="mt-12 space-y-10">
          <Section title="1. Správce údajů">
            <p>
              Správcem osobních údajů je společnost JVM YORE s.r.o., IČO: [bude doplněno],
              se sídlem [bude doplněno] (dále jen „Správce&quot;), provozovatel platformy Makables
              na adrese makables.cz.
            </p>
            <p>
              Kontakt pro záležitosti ochrany osobních údajů: info@makables.cz
            </p>
          </Section>

          <Section title="2. Účel zpracování">
            <p>Vaše osobní údaje zpracováváme za těmito účely:</p>
            <ul className="list-disc space-y-2 pl-5">
              <li>Registrace a správa uživatelského účtu</li>
              <li>Zpracování a doručení objednávek</li>
              <li>Komunikace o stavu objednávky (email notifikace)</li>
              <li>Vystavování faktur a účetních dokladů</li>
              <li>Zlepšování služeb a zákaznická podpora</li>
              <li>Plnění zákonných povinností</li>
            </ul>
          </Section>

          <Section title="3. Rozsah zpracovávaných údajů">
            <p><strong className="text-zinc-300">Zákazníci:</strong> jméno, email, telefon, doručovací adresa.</p>
            <p><strong className="text-zinc-300">Makeři:</strong> jméno, email, telefon, IČO, DIČ, název firmy, adresa sídla, bankovní účet.</p>
            <p>
              Údaje o platebních kartách nezpracováváme — platby zajišťuje platební brána Comgate,
              která je certifikovaná podle standardu PCI DSS.
            </p>
          </Section>

          <Section title="4. Právní základ">
            <ul className="list-disc space-y-2 pl-5">
              <li><strong className="text-zinc-300">Plnění smlouvy</strong> — zpracování objednávek, doručení, fakturace.</li>
              <li><strong className="text-zinc-300">Oprávněný zájem</strong> — zlepšování služeb, prevence podvodů.</li>
              <li><strong className="text-zinc-300">Zákonná povinnost</strong> — účetnictví, daňové doklady.</li>
              <li><strong className="text-zinc-300">Souhlas</strong> — marketingová komunikace (kdykoli odvolatelný).</li>
            </ul>
          </Section>

          <Section title="5. Předávání údajů třetím stranám">
            <p>Vaše údaje předáváme pouze těmto stranám:</p>
            <ul className="list-disc space-y-2 pl-5">
              <li><strong className="text-zinc-300">Zásilkovna (Packeta)</strong> — pro doručení zásilek (jméno, adresa, telefon).</li>
              <li><strong className="text-zinc-300">Comgate</strong> — pro zpracování plateb.</li>
              <li><strong className="text-zinc-300">Supabase</strong> — pro ukládání dat (servery v EU).</li>
              <li><strong className="text-zinc-300">Resend</strong> — pro odesílání emailů.</li>
            </ul>
            <p>Vaše údaje neprodáváme a nepředáváme pro marketingové účely třetích stran.</p>
          </Section>

          <Section title="6. Doba uchovávání">
            <p>
              Osobní údaje uchováváme po dobu trvání účtu a dále po dobu vyžadovanou zákonem
              (typicky 10 let pro účetní doklady). Po smazání účtu jsou osobní údaje anonymizovány
              do 30 dnů, s výjimkou údajů nutných pro zákonné povinnosti.
            </p>
          </Section>

          <Section title="7. Vaše práva">
            <p>Máte právo na:</p>
            <ul className="list-disc space-y-2 pl-5">
              <li><strong className="text-zinc-300">Přístup</strong> — zjistit, jaké údaje o vás zpracováváme.</li>
              <li><strong className="text-zinc-300">Opravu</strong> — aktualizovat nepřesné údaje.</li>
              <li><strong className="text-zinc-300">Výmaz</strong> — požádat o smazání údajů (právo být zapomenut).</li>
              <li><strong className="text-zinc-300">Přenositelnost</strong> — získat své údaje ve strojově čitelném formátu.</li>
              <li><strong className="text-zinc-300">Námitku</strong> — vznést námitku proti zpracování.</li>
              <li><strong className="text-zinc-300">Odvolání souhlasu</strong> — kdykoli odvolat souhlas s marketingem.</li>
            </ul>
            <p>
              Pro uplatnění svých práv nás kontaktujte na info@makables.cz. Na vaši žádost odpovíme
              do 30 dnů.
            </p>
          </Section>

          <Section title="8. Cookies">
            <p>
              Platforma používá pouze nezbytné cookies pro zajištění funkčnosti (přihlášení,
              session). Nepoužíváme analytické ani marketingové cookies třetích stran.
            </p>
          </Section>

          <Section title="9. Kontakt">
            <p>
              V případě dotazů ohledně ochrany osobních údajů nás kontaktujte:
            </p>
            <p>
              Email: info@makables.cz
            </p>
            <p>
              Dozorový úřad: Úřad pro ochranu osobních údajů (ÚOOÚ), www.uoou.cz
            </p>
          </Section>
        </div>
      </div>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <h2 className="text-xl font-semibold text-white">{title}</h2>
      <div className="mt-4 space-y-3 text-sm leading-relaxed text-zinc-400">
        {children}
      </div>
    </div>
  );
}
