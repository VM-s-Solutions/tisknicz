import Link from 'next/link';

export function Footer() {
  return (
    <footer className="bg-zinc-950 text-zinc-400">
      <div className="mx-auto max-w-7xl px-4 py-16 sm:px-6 lg:px-8">
        <div className="grid grid-cols-1 gap-8 md:grid-cols-4">
          <div>
            <Link href="/" className="text-xl font-extrabold tracking-tight">
              <span className="text-gradient-light">Tiskni</span>
              <span className="text-white">.cz</span>
            </Link>
            <p className="mt-4 text-sm leading-relaxed text-zinc-500">
              Marketplace pro tiskaře a makery v ČR. Najdi lokálního tvůrce, objednej, nech si doručit.
            </p>
          </div>

          <div>
            <h4 className="text-sm font-semibold uppercase tracking-wider text-zinc-300">Pro zákazníky</h4>
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
            <h4 className="text-sm font-semibold uppercase tracking-wider text-zinc-300">Pro tiskaře</h4>
            <ul className="mt-4 space-y-3">
              <li>
                <Link href="/pro-tiskare" className="text-sm text-zinc-500 transition-colors hover:text-brand-400">Začít prodávat</Link>
              </li>
              <li>
                <Link href="/auth/register" className="text-sm text-zinc-500 transition-colors hover:text-brand-400">Registrace</Link>
              </li>
            </ul>
          </div>

          <div>
            <h4 className="text-sm font-semibold uppercase tracking-wider text-zinc-300">Informace</h4>
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
          &copy; {new Date().getFullYear()} Tiskni.cz — JVM YORE s.r.o.
        </div>
      </div>
    </footer>
  );
}
