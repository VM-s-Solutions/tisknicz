import { Spinner } from '@/components/ui/spinner';

export default function KatalogLoading() {
  return (
    <div className="mx-auto max-w-7xl px-4 py-12 sm:px-6 lg:px-8">
      <div>
        <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Katalog</p>
        <h1 className="mt-2 text-4xl font-bold tracking-tight text-white">Najděte svého makera</h1>
        <p className="mt-3 text-lg text-zinc-500">Prohlédněte si makery ve vašem okolí</p>
      </div>

      <div className="mt-8 flex flex-wrap gap-2">
        {Array.from({ length: 7 }).map((_, i) => (
          <div key={i} className="h-10 w-24 animate-pulse rounded-xl bg-zinc-800" />
        ))}
      </div>

      <div className="mt-10 flex items-center justify-center py-20">
        <Spinner size="lg" />
      </div>
    </div>
  );
}
