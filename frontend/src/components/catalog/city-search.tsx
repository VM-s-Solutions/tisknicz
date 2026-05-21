'use client';

import { useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Icon } from '@/components/ui/icon';

interface CitySearchProps {
  currentCity?: string;
}

export function CitySearch({ currentCity }: CitySearchProps) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [city, setCity] = useState(currentCity ?? '');

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const params = new URLSearchParams(searchParams.toString());
    if (city.trim()) {
      params.set('mesto', city.trim());
    } else {
      params.delete('mesto');
    }
    params.delete('page');
    router.push(`/katalog?${params.toString()}`);
  }

  return (
    <form onSubmit={handleSubmit} className="flex gap-2">
      <div className="relative">
        <Icon name="search" size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-zinc-600" />
        <input
          type="text"
          value={city}
          onChange={(e) => setCity(e.target.value)}
          placeholder="Hledat město..."
          className="rounded-xl border border-zinc-700 bg-zinc-900 py-2.5 pl-9 pr-4 text-sm text-zinc-100 placeholder:text-zinc-600 transition-all duration-200 focus:border-brand-400 focus:outline-none focus:ring-2 focus:ring-brand-400/20"
        />
      </div>
      <button
        type="submit"
        className="rounded-xl bg-zinc-800 px-5 py-2.5 text-sm font-semibold text-zinc-300 transition-all duration-200 hover:bg-zinc-700 hover:text-white"
      >
        Hledat
      </button>
    </form>
  );
}
