'use client';

import { useRouter, useSearchParams } from 'next/navigation';
import type { Category } from '@/types';

interface CategoryFilterProps {
  categories: Category[];
}

export function CategoryFilter({ categories }: CategoryFilterProps) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const activeCategory = searchParams.get('kategorie');

  function handleSelect(slug: string | null) {
    const params = new URLSearchParams(searchParams.toString());
    if (slug) {
      params.set('kategorie', slug);
    } else {
      params.delete('kategorie');
    }
    params.delete('page');
    router.push(`/katalog?${params.toString()}`);
  }

  return (
    <div className="flex flex-wrap gap-2">
      <button
        onClick={() => handleSelect(null)}
        className={`rounded-xl px-4 py-2 text-sm font-semibold transition-all duration-200 ${
          !activeCategory
            ? 'border border-brand-400 bg-brand-400/10 text-brand-400'
            : 'bg-zinc-800 text-zinc-400 hover:bg-zinc-700 hover:text-zinc-200'
        }`}
      >
        Vše
      </button>
      {categories.map((cat) => (
        <button
          key={cat.slug}
          onClick={() => handleSelect(cat.slug)}
          className={`rounded-xl px-4 py-2 text-sm font-semibold transition-all duration-200 ${
            activeCategory === cat.slug
              ? 'border border-brand-400 bg-brand-400/10 text-brand-400'
              : 'bg-zinc-800 text-zinc-400 hover:bg-zinc-700 hover:text-zinc-200'
          }`}
        >
          {cat.name}
        </button>
      ))}
    </div>
  );
}
