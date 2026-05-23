import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'Obchodní podmínky',
  description: 'Obchodní podmínky platformy Makables.',
};

export default function VopPage() {
  return (
    <div className="bg-surface-primary py-20 sm:py-28">
      <div className="mx-auto max-w-3xl px-4 sm:px-6 lg:px-8">
        <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Právní dokumenty</p>
        <h1 className="mt-3 text-3xl font-bold tracking-tight text-white sm:text-4xl">
          Obchodní podmínky
        </h1>
        <p className="mt-4 text-sm text-zinc-500">
          Platné od 1. 1. 2026 &middot; Poslední aktualizace: 1. 5. 2026
        </p>

        <div className="mt-12 space-y-10">
          <Section title="1. Úvodní ustanovení">
            <p>
              Tyto obchodní podmínky (dále jen „Podmínky&quot;) upravují práva a povinnosti mezi provozovatelem
              platformy Makables — společností JVM YORE s.r.o., IČO: [bude doplněno], se sídlem [bude doplněno]
              (dále jen „Provozovatel&quot;) a uživateli platformy (dále jen „Uživatelé&quot;).
            </p>
            <p>
              Platforma Makables (dostupná na adrese makables.cz) je online marketplace propojující zákazníky
              s makery — tvůrci a výrobci nabízejícími služby 3D tisku, laserového řezání, potisku textilu
              a dalších výrobních technologií.
            </p>
          </Section>

          <Section title="2. Definice pojmů">
            <ul className="list-disc space-y-2 pl-5">
              <li><strong className="text-zinc-300">Maker</strong> — registrovaný uživatel s platným IČO, který nabízí produkty nebo služby na platformě.</li>
              <li><strong className="text-zinc-300">Zákazník</strong> — registrovaný uživatel, který objednává produkty nebo služby od makerů.</li>
              <li><strong className="text-zinc-300">Objednávka</strong> — závazný požadavek zákazníka na zhotovení produktu nebo poskytnutí služby makerem.</li>
              <li><strong className="text-zinc-300">Provize</strong> — poplatek ve výši 15 % z ceny objednávky, který si Provozovatel účtuje za zprostředkování.</li>
            </ul>
          </Section>

          <Section title="3. Registrace a účet">
            <p>
              Pro využívání platformy je nutná registrace. Uživatel je povinen uvést pravdivé a aktuální údaje.
              Maker je povinen uvést platné IČO a bankovní účet pro příjem plateb.
            </p>
            <p>
              Provozovatel si vyhrazuje právo odmítnout nebo zrušit registraci makera, pokud nesplňuje
              podmínky platformy nebo porušuje tyto Podmínky.
            </p>
          </Section>

          <Section title="4. Objednávka a platba">
            <p>
              Objednávka je závazná okamžikem zaplacení. Platba probíhá prostřednictvím platební brány
              Comgate (kartou nebo bankovním převodem). Platba je chráněna — maker obdrží úhradu až po
              úspěšném doručení objednávky zákazníkovi.
            </p>
            <p>
              Ceny produktů a služeb si nastavuje maker. Provozovatel si účtuje provizi ve výši 15 %
              z celkové ceny objednávky (bez poštovného).
            </p>
          </Section>

          <Section title="5. Doručení">
            <p>
              Doručení zajišťuje maker prostřednictvím služby Zásilkovna (Packeta) nebo osobním odběrem,
              pokud to maker nabízí. Náklady na doručení hradí zákazník.
            </p>
            <p>
              Maker je povinen odeslat objednávku v dohodnutém termínu. V případě prodlení má zákazník
              právo objednávku zrušit.
            </p>
          </Section>

          <Section title="6. Reklamace a vrácení">
            <p>
              Zákazník má právo reklamovat vadný produkt do 14 dnů od převzetí. Reklamaci řeší zákazník
              přímo s makerem prostřednictvím platformy. Provozovatel poskytuje podporu při řešení sporů.
            </p>
          </Section>

          <Section title="7. Odpovědnost">
            <p>
              Provozovatel je zprostředkovatelem a nenese odpovědnost za kvalitu produktů nebo služeb
              poskytovaných makery. Maker odpovídá za kvalitu svých produktů a dodržení dohodnutých podmínek.
            </p>
          </Section>

          <Section title="8. Závěrečná ustanovení">
            <p>
              Tyto Podmínky se řídí právním řádem České republiky. Provozovatel si vyhrazuje právo
              Podmínky jednostranně měnit. O změnách bude Uživatele informovat prostřednictvím emailu
              nebo oznámení na platformě.
            </p>
            <p>
              Kontakt: info@makables.cz
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
