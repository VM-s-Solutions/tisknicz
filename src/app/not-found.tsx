import Link from 'next/link';
import { Icon } from '@/components/ui/icon';

export default function NotFound() {
  return (
    <div className="flex min-h-[calc(100vh-64px)] items-center justify-center bg-surface-primary px-4">
      <div className="text-center">
        <p className="text-8xl font-bold tracking-tight text-zinc-800">404</p>
        <h1 className="mt-4 text-2xl font-bold tracking-tight text-white sm:text-3xl">
          Stránka nenalezena
        </h1>
        <p className="mt-3 text-base text-zinc-500">
          Tato stránka neexistuje nebo byla přesunuta.
        </p>
        <div className="mt-8 flex items-center justify-center gap-4">
          <Link
            href="/"
            className="group inline-flex items-center gap-2 rounded-xl border border-brand-400/50 px-6 py-3 text-sm font-semibold text-brand-400 transition-all duration-200 hover:bg-brand-400/10 hover:border-brand-400"
          >
            <Icon name="arrowLeft" size={16} />
            Zpět na úvod
          </Link>
          <Link
            href="/katalog"
            className="inline-flex items-center gap-2 rounded-xl border border-zinc-700 px-6 py-3 text-sm font-semibold text-zinc-400 transition-all duration-200 hover:border-zinc-500 hover:text-white"
          >
            Prohlédnout katalog
          </Link>
        </div>
      </div>
    </div>
  );
}
