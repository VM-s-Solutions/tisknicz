'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Icon } from '@/components/ui/icon';

interface OrderActionsProps {
  orderId: string;
  status: string;
  role: 'maker' | 'customer';
}

export function OrderActions({ orderId, status, role }: OrderActionsProps) {
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  async function performAction(action: string) {
    setLoading(true);
    setError('');

    try {
      const res = await fetch(`/api/orders/${orderId}/${action}`, {
        method: 'POST',
      });

      if (!res.ok) {
        const data = await res.json();
        setError(data.error ?? 'Chyba při aktualizaci');
        setLoading(false);
        return;
      }

      router.refresh();
    } catch {
      setError('Nepodařilo se provést akci.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="flex flex-col items-end gap-2">
      {role === 'maker' && status === 'paid' && (
        <button
          onClick={() => performAction('accept')}
          disabled={loading}
          className="inline-flex items-center gap-2 rounded-xl bg-gradient-to-r from-brand-600 to-brand-500 px-5 py-2.5 text-sm font-semibold text-white shadow-md transition-all duration-200 hover:shadow-lg hover:scale-[1.02] disabled:opacity-50"
        >
          {loading ? (
            <Spinner />
          ) : (
            <Icon name="check" size={16} />
          )}
          Přijmout objednávku
        </button>
      )}

      {role === 'maker' && status === 'accepted' && (
        <button
          onClick={() => performAction('ship')}
          disabled={loading}
          className="inline-flex items-center gap-2 rounded-xl bg-gradient-to-r from-brand-600 to-brand-500 px-5 py-2.5 text-sm font-semibold text-white shadow-md transition-all duration-200 hover:shadow-lg hover:scale-[1.02] disabled:opacity-50"
        >
          {loading ? (
            <Spinner />
          ) : (
            <Icon name="truck" size={16} />
          )}
          Označit jako odesláno
        </button>
      )}

      {role === 'customer' && status === 'shipped' && (
        <button
          onClick={() => performAction('deliver')}
          disabled={loading}
          className="inline-flex items-center gap-2 rounded-xl bg-gradient-to-r from-brand-600 to-brand-500 px-5 py-2.5 text-sm font-semibold text-white shadow-md transition-all duration-200 hover:shadow-lg hover:scale-[1.02] disabled:opacity-50"
        >
          {loading ? (
            <Spinner />
          ) : (
            <Icon name="checkCircle" size={16} />
          )}
          Potvrdit doručení
        </button>
      )}

      {error && (
        <p className="text-sm text-red-600 flex items-center gap-1">
          <Icon name="xCircle" size={14} />
          {error}
        </p>
      )}
    </div>
  );
}

function Spinner() {
  return (
    <svg className="h-4 w-4 animate-spin" viewBox="0 0 24 24" fill="none">
      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
    </svg>
  );
}
