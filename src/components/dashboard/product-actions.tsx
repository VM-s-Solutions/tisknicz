'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Button } from '@/components/ui/button';
import { ProductForm } from '@/components/forms/product-form';
import type { Category, Product } from '@/types';

interface ProductActionsProps {
  categories: Category[];
  productId?: string;
  product?: Product;
  isEdit?: boolean;
}

export function ProductActions({ categories, productId, product, isEdit }: ProductActionsProps) {
  const router = useRouter();
  const [showForm, setShowForm] = useState(false);
  const [deleting, setDeleting] = useState(false);

  async function handleDelete() {
    if (!productId) return;
    if (!confirm('Opravdu chcete smazat tento produkt?')) return;

    setDeleting(true);
    try {
      const res = await fetch(`/api/products/${productId}`, { method: 'DELETE' });
      if (res.ok) {
        router.refresh();
      }
    } finally {
      setDeleting(false);
    }
  }

  if (showForm) {
    return (
      <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
        <div className="w-full max-w-lg rounded-2xl border border-zinc-800 bg-surface-card p-6 shadow-xl">
          <div className="mb-4 flex items-center justify-between">
            <h2 className="text-lg font-semibold text-white">
              {isEdit ? 'Upravit produkt' : 'Nový produkt'}
            </h2>
            <button
              onClick={() => setShowForm(false)}
              className="text-zinc-500 hover:text-zinc-300 transition-colors"
            >
              &times;
            </button>
          </div>
          <ProductForm categories={categories} product={product} />
        </div>
      </div>
    );
  }

  if (isEdit) {
    return (
      <div className="flex items-center gap-2">
        <Button variant="outline" size="sm" onClick={() => setShowForm(true)}>
          Upravit
        </Button>
        <Button variant="danger" size="sm" loading={deleting} onClick={handleDelete}>
          Smazat
        </Button>
      </div>
    );
  }

  return (
    <Button onClick={() => setShowForm(true)}>
      Přidat produkt
    </Button>
  );
}
