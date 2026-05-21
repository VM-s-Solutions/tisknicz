'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Select } from '@/components/ui/select';
import { Alert } from '@/components/ui/alert';
import type { Category, Product } from '@/types';

interface ProductFormProps {
  categories: Category[];
  product?: Product;
}

export function ProductForm({ categories, product }: ProductFormProps) {
  const router = useRouter();
  const isEdit = !!product;

  const [title, setTitle] = useState(product?.title ?? '');
  const [description, setDescription] = useState(product?.description ?? '');
  const [price, setPrice] = useState(product?.price?.toString() ?? '');
  const [priceType, setPriceType] = useState<string>(product?.price_type ?? 'fixed');
  const [categoryId, setCategoryId] = useState(product?.category_id ?? '');
  const [weightGrams, setWeightGrams] = useState(product?.weight_grams?.toString() ?? '');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setLoading(true);

    const body = {
      title,
      description: description || undefined,
      price: parseInt(price, 10) || 0,
      price_type: priceType,
      category_id: categoryId || undefined,
      weight_grams: weightGrams ? parseInt(weightGrams, 10) : undefined,
    };

    try {
      const url = isEdit ? `/api/products/${product.id}` : '/api/products';
      const method = isEdit ? 'PATCH' : 'POST';

      const res = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });

      if (!res.ok) {
        const data = await res.json();
        setError(typeof data.error === 'string' ? data.error : 'Chyba při ukládání produktu.');
        return;
      }

      router.push('/dashboard/maker/produkty');
      router.refresh();
    } catch {
      setError('Nastala neočekávaná chyba.');
    } finally {
      setLoading(false);
    }
  }

  const categoryOptions = categories.map((c) => ({ value: c.id, label: `${c.icon ?? ''} ${c.name}`.trim() }));
  const priceTypeOptions = [
    { value: 'fixed', label: 'Pevná cena' },
    { value: 'from', label: 'Cena od' },
    { value: 'on_request', label: 'Na dotaz' },
  ];

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4">
      <Input
        label="Název produktu"
        value={title}
        onChange={(e) => setTitle(e.target.value)}
        placeholder="Např. 3D tisk na zakázku"
        required
      />
      <Textarea
        label="Popis"
        value={description}
        onChange={(e) => setDescription(e.target.value)}
        placeholder="Popište produkt nebo službu..."
        rows={4}
      />

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Input
          label="Cena (Kč)"
          type="number"
          value={price}
          onChange={(e) => setPrice(e.target.value)}
          placeholder="0"
          min={0}
        />
        <Select
          label="Typ ceny"
          value={priceType}
          onChange={(e) => setPriceType(e.target.value)}
          options={priceTypeOptions}
        />
      </div>

      <Select
        label="Kategorie"
        value={categoryId}
        onChange={(e) => setCategoryId(e.target.value)}
        options={categoryOptions}
        placeholder="Vyberte kategorii"
      />

      <Input
        label="Hmotnost (g)"
        type="number"
        value={weightGrams}
        onChange={(e) => setWeightGrams(e.target.value)}
        placeholder="Pro výpočet poštovného"
        min={1}
      />

      {error && <Alert variant="error">{error}</Alert>}

      <div className="flex gap-3">
        <Button type="submit" loading={loading}>
          {isEdit ? 'Uložit změny' : 'Vytvořit produkt'}
        </Button>
        <Button type="button" variant="ghost" onClick={() => router.back()}>
          Zrušit
        </Button>
      </div>
    </form>
  );
}
