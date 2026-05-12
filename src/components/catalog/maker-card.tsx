import Link from 'next/link';
import { Icon } from '@/components/ui/icon';

interface MakerCardProps {
  maker: {
    id: string;
    company_name: string;
    city: string;
    bio: string | null;
    rating_avg: number;
    rating_count: number;
    total_orders: number;
    is_verified: boolean;
    ico: string;
  };
  slug: string;
}

export function MakerCard({ maker, slug }: MakerCardProps) {
  return (
    <Link href={`/katalog/${slug}`} className="group block">
      <div className="overflow-hidden rounded-2xl border border-zinc-100 bg-white shadow-md transition-all duration-300 group-hover:-translate-y-1 group-hover:shadow-xl group-hover:border-brand-200">
        {/* Gradient header */}
        <div className="relative bg-gradient-to-br from-brand-600 via-brand-500 to-brand-400 p-6">
          <div className="absolute inset-0 bg-dot-pattern opacity-10" />
          <div className="relative flex items-center gap-4">
            <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-white/20 text-xl font-bold text-white backdrop-blur-sm">
              {maker.company_name.charAt(0)}
            </div>
            <div className="min-w-0">
              <div className="flex items-center gap-2">
                <h3 className="truncate text-lg font-bold text-white">{maker.company_name}</h3>
                {maker.is_verified && (
                  <Icon name="verified" size={18} className="shrink-0 text-brand-200" />
                )}
              </div>
              <div className="mt-0.5 flex items-center gap-1.5 text-sm text-brand-100">
                <Icon name="mapPin" size={14} />
                <span>{maker.city}</span>
              </div>
            </div>
          </div>
        </div>

        {/* Content */}
        <div className="p-5">
          {maker.bio && (
            <p className="line-clamp-2 text-sm leading-relaxed text-zinc-500">{maker.bio}</p>
          )}

          <div className="mt-4 flex items-center justify-between">
            {/* Rating */}
            <div className="flex items-center gap-1.5">
              {maker.rating_count > 0 ? (
                <>
                  <div className="flex items-center gap-0.5">
                    {Array.from({ length: 5 }, (_, i) => (
                      <Icon
                        key={i}
                        name="star"
                        size={14}
                        className={i < Math.round(maker.rating_avg) ? 'text-accent-400' : 'text-zinc-200'}
                      />
                    ))}
                  </div>
                  <span className="text-xs font-medium text-zinc-500">({maker.rating_count})</span>
                </>
              ) : (
                <span className="text-xs text-zinc-400">Zatím bez hodnocení</span>
              )}
            </div>

            {/* Orders */}
            <div className="flex items-center gap-1 text-xs text-zinc-400">
              <Icon name="package" size={14} />
              <span>{maker.total_orders}</span>
            </div>
          </div>
        </div>
      </div>
    </Link>
  );
}
