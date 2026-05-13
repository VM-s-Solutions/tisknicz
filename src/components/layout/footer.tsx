import Link from 'next/link';

export function Footer() {
  return (
    <footer className="border-t border-zinc-800/50 bg-black text-zinc-500">
      <div className="mx-auto max-w-7xl px-4 py-16 sm:px-6 lg:px-8">
        <div className="grid grid-cols-1 gap-8 md:grid-cols-4">
          <div>
            <Link href="/" className="text-xl font-bold tracking-tight text-white">
              Makables
            </Link>
            <p className="mt-4 text-sm leading-relaxed text-zinc-500">
              Where Ideas Take Shape.
            </p>
            <p className="mt-2 text-sm leading-relaxed text-zinc-600">
              Marketplace pro makery a tiskaře v ČR.
            </p>
          </div>

          <div>
            <h4 className="text-sm font-semibold uppercase tracking-wider text-zinc-400">Pro zákazníky</h4>
            <ul className="mt-4 space-y-3">
              <li>
                <Link href="/katalog" className="text-sm text-zinc-500 transition-colors hover:text-brand-400">Katalog</Link>
              </li>
              <li>
                <Link href="/jak-to-funguje" className="text-sm text-zinc-500 transition-colors hover:text-brand-400">Jak to funguje</Link>
              </li>
            </ul>
          </div>

          <div>
            <h4 className="text-sm font-semibold uppercase tracking-wider text-zinc-400">Pro makery</h4>
            <ul className="mt-4 space-y-3">
              <li>
                <Link href="/pro-makery" className="text-sm text-zinc-500 transition-colors hover:text-brand-400">Začít prodávat</Link>
              </li>
              <li>
                <Link href="/auth/register" className="text-sm text-zinc-500 transition-colors hover:text-brand-400">Registrace</Link>
              </li>
            </ul>
          </div>

          <div>
            <h4 className="text-sm font-semibold uppercase tracking-wider text-zinc-400">Informace</h4>
            <ul className="mt-4 space-y-3">
              <li>
                <Link href="/vop" className="text-sm text-zinc-500 transition-colors hover:text-brand-400">Obchodní podmínky</Link>
              </li>
              <li>
                <Link href="/gdpr" className="text-sm text-zinc-500 transition-colors hover:text-brand-400">Ochrana údajů</Link>
              </li>
            </ul>
          </div>
        </div>

        <div className="mt-12 border-t border-zinc-800 pt-8 text-center text-sm text-zinc-600">
          &copy; {new Date().getFullYear()} Makables &mdash; JVM YORE s.r.o.
        </div>
      </div>
    </footer>
  );
}
