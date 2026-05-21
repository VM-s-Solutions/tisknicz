'use client';

import { Suspense, useState, useEffect } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import { formatCurrency, calculateOrderPricing } from '@/lib/utils/pricing';
import { DEMO_PRODUCTS, DEMO_MAKERS } from '@/lib/demo-data';

interface FormData {
  customer_name: string;
  customer_email: string;
  customer_phone: string;
  description: string;
  quantity: number;
  shipping_method: 'zasilkovna' | 'personal_pickup';
  zasilkovna_branch_id: string;
  zasilkovna_branch_name: string;
}

const SHIPPING_PRICE = 89;

export default function OrderPage() {
  return (
    <Suspense fallback={<OrderPageSkeleton />}>
      <OrderPageContent />
    </Suspense>
  );
}

function OrderPageSkeleton() {
  return (
    <div className="mx-auto max-w-4xl px-4 py-12 sm:px-6 lg:px-8">
      <div className="h-6 w-48 animate-pulse rounded bg-zinc-800" />
      <div className="mt-4 h-10 w-80 animate-pulse rounded bg-zinc-800" />
      <div className="mt-10 grid grid-cols-1 gap-8 lg:grid-cols-5">
        <div className="lg:col-span-3 space-y-8">
          <div className="h-64 animate-pulse rounded-2xl bg-zinc-800" />
          <div className="h-48 animate-pulse rounded-2xl bg-zinc-800" />
        </div>
        <div className="lg:col-span-2">
          <div className="h-72 animate-pulse rounded-2xl bg-zinc-800" />
        </div>
      </div>
    </div>
  );
}

function OrderPageContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const productId = searchParams.get('produkt');
  const makerId = searchParams.get('maker');
  const isCustomQuote = searchParams.get('custom') === '1';

  const [form, setForm] = useState<FormData>({
    customer_name: '',
    customer_email: '',
    customer_phone: '',
    description: '',
    quantity: 1,
    shipping_method: 'zasilkovna',
    zasilkovna_branch_id: '',
    zasilkovna_branch_name: '',
  });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  // Find product/maker info (demo data fallback)
  const product = productId ? DEMO_PRODUCTS.find((p) => p.id === productId) : null;
  const maker = makerId ? DEMO_MAKERS.find((m) => m.id === makerId) : null;

  const productPrice = product ? product.price * form.quantity : 0;
  const shippingPrice = form.shipping_method === 'zasilkovna' ? SHIPPING_PRICE : 0;
  const pricing = calculateOrderPricing(productPrice, shippingPrice);

  function updateField<K extends keyof FormData>(key: K, value: FormData[K]) {
    setForm((prev) => ({ ...prev, [key]: value }));
  }

  // Zásilkovna widget callback
  useEffect(() => {
    function handlePacketaMessage(event: MessageEvent) {
      if (event.data && event.data.packetaWidgetType === 'point-selected') {
        const point = event.data.point;
        if (point) {
          updateField('zasilkovna_branch_id', String(point.id));
          updateField('zasilkovna_branch_name', point.name || '');
        }
      }
    }
    window.addEventListener('message', handlePacketaMessage);
    return () => window.removeEventListener('message', handlePacketaMessage);
  }, []);

  function openPacketaWidget() {
    const Packeta = (window as unknown as Record<string, unknown>).Packeta as {
      Widget?: { pick: (apiKey: string, callback: (point: { id: number; name: string } | null) => void, opts?: Record<string, unknown>) => void };
    } | undefined;

    if (Packeta?.Widget) {
      Packeta.Widget.pick(
        process.env.NEXT_PUBLIC_ZASILKOVNA_API_KEY ?? '',
        (point) => {
          if (point) {
            setForm((prev) => ({
              ...prev,
              zasilkovna_branch_id: String(point.id),
              zasilkovna_branch_name: point.name,
            }));
          }
        },
        { language: 'cs' }
      );
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');

    if (!form.customer_name || !form.customer_email || !form.customer_phone) {
      setError('Vyplňte prosím všechny kontaktní údaje.');
      return;
    }

    if (form.shipping_method === 'zasilkovna' && !form.zasilkovna_branch_id) {
      setError('Vyberte prosím výdejní místo Zásilkovny.');
      return;
    }

    setLoading(true);

    try {
      const res = await fetch('/api/orders', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          product_id: productId,
          maker_id: makerId,
          title: product?.title ?? 'Vlastní objednávka',
          description: form.description || undefined,
          quantity: form.quantity,
          product_price: productPrice,
          shipping_method: form.shipping_method,
          shipping_price: shippingPrice,
          zasilkovna_branch_id: form.zasilkovna_branch_id || undefined,
          zasilkovna_branch_name: form.zasilkovna_branch_name || undefined,
          customer_name: form.customer_name,
          customer_email: form.customer_email,
          customer_phone: form.customer_phone,
        }),
      });

      if (!res.ok) {
        const data = await res.json();
        setError(data.error ?? 'Chyba při vytváření objednávky');
        setLoading(false);
        return;
      }

      const order = await res.json();
      router.push(`/objednavka/potvrzeni?id=${order.id}&cislo=${order.order_number}`);
    } catch {
      setError('Nepodařilo se odeslat objednávku. Zkuste to znovu.');
      setLoading(false);
    }
  }

  if (!product || !maker) {
    return (
      <div className="mx-auto max-w-3xl px-4 py-16 text-center sm:px-6">
        <Icon name="alertCircle" size={48} className="mx-auto text-zinc-600" />
        <h1 className="mt-4 text-xl font-bold text-white">Produkt nenalezen</h1>
        <p className="mt-2 text-zinc-500">Tento produkt nebo maker neexistuje.</p>
        <Link href="/katalog" className="mt-6 inline-flex items-center gap-2 text-sm font-semibold text-brand-400 hover:text-brand-300">
          <Icon name="arrowLeft" size={16} />
          Zpět do katalogu
        </Link>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-4xl px-4 py-12 sm:px-6 lg:px-8">
      {/* Breadcrumb */}
      <nav className="flex items-center gap-2 text-sm text-zinc-600 mb-8">
        <Link href="/katalog" className="hover:text-zinc-400 transition-colors">Katalog</Link>
        <Icon name="arrowRight" size={12} />
        <Link href={`/produkt/${product.id}`} className="hover:text-zinc-400 transition-colors">{product.title}</Link>
        <Icon name="arrowRight" size={12} />
        <span className="text-zinc-400">{isCustomQuote ? 'Poptávka' : 'Objednávka'}</span>
      </nav>

      <h1 className="text-3xl font-bold tracking-tight text-white">
        {isCustomQuote ? 'Poptat cenovou nabídku' : 'Vytvořit objednávku'}
      </h1>
      <p className="mt-2 text-zinc-500">
        {product.title} — {maker.company_name}
      </p>

      <form onSubmit={handleSubmit} className="mt-10 grid grid-cols-1 gap-8 lg:grid-cols-5">
        {/* Left — form fields */}
        <div className="lg:col-span-3 space-y-8">
          {/* Contact info */}
          <section className="rounded-2xl border border-zinc-800 bg-surface-card p-6">
            <h2 className="text-lg font-semibold text-white flex items-center gap-2">
              <Icon name="users" size={20} className="text-brand-400" />
              Kontaktní údaje
            </h2>
            <div className="mt-4 space-y-4">
              <div>
                <label htmlFor="name" className="block text-sm font-medium text-zinc-400">Jméno a příjmení</label>
                <input
                  id="name"
                  type="text"
                  value={form.customer_name}
                  onChange={(e) => updateField('customer_name', e.target.value)}
                  className="mt-1 block w-full rounded-xl border border-zinc-700 bg-zinc-900 px-4 py-2.5 text-zinc-100 transition-colors focus:border-brand-400 focus:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-brand-400/20 placeholder:text-zinc-600"
                  placeholder="Jan Novák"
                  required
                />
              </div>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div>
                  <label htmlFor="email" className="block text-sm font-medium text-zinc-400">E-mail</label>
                  <input
                    id="email"
                    type="email"
                    value={form.customer_email}
                    onChange={(e) => updateField('customer_email', e.target.value)}
                    className="mt-1 block w-full rounded-xl border border-zinc-700 bg-zinc-900 px-4 py-2.5 text-zinc-100 transition-colors focus:border-brand-400 focus:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-brand-400/20 placeholder:text-zinc-600"
                    placeholder="jan@email.cz"
                    required
                  />
                </div>
                <div>
                  <label htmlFor="phone" className="block text-sm font-medium text-zinc-400">Telefon</label>
                  <input
                    id="phone"
                    type="tel"
                    value={form.customer_phone}
                    onChange={(e) => updateField('customer_phone', e.target.value)}
                    className="mt-1 block w-full rounded-xl border border-zinc-700 bg-zinc-900 px-4 py-2.5 text-zinc-100 transition-colors focus:border-brand-400 focus:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-brand-400/20 placeholder:text-zinc-600"
                    placeholder="+420 123 456 789"
                    required
                  />
                </div>
              </div>
            </div>
          </section>

          {/* Order details */}
          <section className="rounded-2xl border border-zinc-800 bg-surface-card p-6">
            <h2 className="text-lg font-semibold text-white flex items-center gap-2">
              <Icon name="file" size={20} className="text-brand-400" />
              Detail objednávky
            </h2>
            <div className="mt-4 space-y-4">
              {!isCustomQuote && (
                <div>
                  <label htmlFor="quantity" className="block text-sm font-medium text-zinc-400">Množství</label>
                  <input
                    id="quantity"
                    type="number"
                    min={1}
                    max={1000}
                    value={form.quantity}
                    onChange={(e) => updateField('quantity', Math.max(1, parseInt(e.target.value, 10) || 1))}
                    className="mt-1 block w-24 rounded-xl border border-zinc-700 bg-zinc-900 px-4 py-2.5 text-zinc-100 transition-colors focus:border-brand-400 focus:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-brand-400/20"
                  />
                </div>
              )}
              <div>
                <label htmlFor="description" className="block text-sm font-medium text-zinc-400">
                  {isCustomQuote ? 'Popište svůj požadavek' : 'Poznámka k objednávce (volitelné)'}
                </label>
                <textarea
                  id="description"
                  rows={4}
                  value={form.description}
                  onChange={(e) => updateField('description', e.target.value)}
                  className="mt-1 block w-full rounded-xl border border-zinc-700 bg-zinc-900 px-4 py-2.5 text-zinc-100 transition-colors focus:border-brand-400 focus:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-brand-400/20 resize-none placeholder:text-zinc-600"
                  placeholder={isCustomQuote ? 'Popište co potřebujete, rozměry, materiály, množství...' : 'Speciální požadavky, barva, materiál...'}
                />
              </div>
            </div>
          </section>

          {/* Shipping */}
          {!isCustomQuote && (
            <section className="rounded-2xl border border-zinc-800 bg-surface-card p-6">
              <h2 className="text-lg font-semibold text-white flex items-center gap-2">
                <Icon name="truck" size={20} className="text-brand-400" />
                Doručení
              </h2>
              <div className="mt-4 space-y-3">
                <label className={`flex cursor-pointer items-start gap-4 rounded-xl border p-4 transition-all ${form.shipping_method === 'zasilkovna' ? 'border-brand-400/50 bg-brand-400/5' : 'border-zinc-700 hover:border-zinc-600'}`}>
                  <input
                    type="radio"
                    name="shipping"
                    value="zasilkovna"
                    checked={form.shipping_method === 'zasilkovna'}
                    onChange={() => updateField('shipping_method', 'zasilkovna')}
                    className="mt-1 h-4 w-4 text-brand-400 focus:ring-brand-400 bg-zinc-800 border-zinc-600"
                  />
                  <div className="flex-1">
                    <span className="font-medium text-zinc-200">Zásilkovna</span>
                    <p className="text-sm text-zinc-500">Výdejní místo — {formatCurrency(SHIPPING_PRICE)}</p>
                  </div>
                </label>

                {form.shipping_method === 'zasilkovna' && (
                  <div className="ml-8">
                    {form.zasilkovna_branch_name ? (
                      <div className="flex items-center gap-3 rounded-xl bg-brand-400/5 border border-brand-400/20 px-4 py-3">
                        <Icon name="checkCircle" size={18} className="text-brand-400 shrink-0" />
                        <div className="flex-1">
                          <p className="text-sm font-medium text-zinc-200">{form.zasilkovna_branch_name}</p>
                          <p className="text-xs text-zinc-500">ID: {form.zasilkovna_branch_id}</p>
                        </div>
                        <button
                          type="button"
                          onClick={openPacketaWidget}
                          className="text-sm font-medium text-brand-400 hover:text-brand-300"
                        >
                          Změnit
                        </button>
                      </div>
                    ) : (
                      <button
                        type="button"
                        onClick={openPacketaWidget}
                        className="flex items-center gap-2 rounded-xl border border-dashed border-zinc-600 px-4 py-3 text-sm font-medium text-zinc-400 transition-colors hover:border-brand-400/50 hover:text-brand-400"
                      >
                        <Icon name="mapPin" size={16} />
                        Vybrat výdejní místo
                      </button>
                    )}
                  </div>
                )}

                <label className={`flex cursor-pointer items-start gap-4 rounded-xl border p-4 transition-all ${form.shipping_method === 'personal_pickup' ? 'border-brand-400/50 bg-brand-400/5' : 'border-zinc-700 hover:border-zinc-600'}`}>
                  <input
                    type="radio"
                    name="shipping"
                    value="personal_pickup"
                    checked={form.shipping_method === 'personal_pickup'}
                    onChange={() => updateField('shipping_method', 'personal_pickup')}
                    className="mt-1 h-4 w-4 text-brand-400 focus:ring-brand-400 bg-zinc-800 border-zinc-600"
                  />
                  <div className="flex-1">
                    <span className="font-medium text-zinc-200">Osobní odběr</span>
                    <p className="text-sm text-zinc-500">Vyzvednete přímo u makera — zdarma</p>
                  </div>
                </label>
              </div>
            </section>
          )}
        </div>

        {/* Right — summary */}
        <div className="lg:col-span-2">
          <div className="sticky top-24 rounded-2xl border border-zinc-800 bg-surface-card p-6">
            <h2 className="text-lg font-semibold text-white">Shrnutí</h2>

            <div className="mt-4 space-y-1">
              <div className="flex items-start gap-3">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-zinc-700 to-zinc-800 text-sm font-bold text-white">
                  {maker.company_name.charAt(0)}
                </div>
                <div>
                  <p className="font-medium text-zinc-200">{product.title}</p>
                  <p className="text-sm text-zinc-500">{maker.company_name}</p>
                </div>
              </div>
            </div>

            {!isCustomQuote && (
              <div className="mt-6 space-y-3 border-t border-zinc-800 pt-4">
                <div className="flex justify-between text-sm">
                  <span className="text-zinc-500">Cena produktu</span>
                  <span className="text-zinc-300">
                    {form.quantity > 1 ? `${form.quantity} × ` : ''}
                    {formatCurrency(product.price)}
                  </span>
                </div>
                {form.quantity > 1 && (
                  <div className="flex justify-between text-sm">
                    <span className="text-zinc-500">Mezisoučet</span>
                    <span className="text-zinc-300">{formatCurrency(productPrice)}</span>
                  </div>
                )}
                <div className="flex justify-between text-sm">
                  <span className="text-zinc-500">Doprava</span>
                  <span className="text-zinc-300">
                    {shippingPrice > 0 ? formatCurrency(shippingPrice) : 'Zdarma'}
                  </span>
                </div>
                <div className="flex justify-between border-t border-zinc-800 pt-3 text-base font-semibold">
                  <span className="text-white">Celkem</span>
                  <span className="text-brand-400">{formatCurrency(pricing.total_price)}</span>
                </div>
              </div>
            )}

            {isCustomQuote && (
              <div className="mt-6 rounded-xl bg-zinc-900 border border-zinc-800 p-4">
                <p className="text-sm text-zinc-400">
                  Maker vám po obdržení poptávky pošle cenovou nabídku.
                </p>
              </div>
            )}

            {error && (
              <div className="mt-4 rounded-xl bg-red-950/50 border border-red-900/50 px-4 py-3 flex items-start gap-2">
                <Icon name="xCircle" size={16} className="text-red-400 mt-0.5 shrink-0" />
                <p className="text-sm text-red-300">{error}</p>
              </div>
            )}

            <button
              type="submit"
              disabled={loading}
              className="mt-6 flex w-full items-center justify-center gap-2 rounded-xl border border-brand-400 bg-brand-400/10 px-6 py-3.5 text-base font-semibold text-brand-400 transition-all duration-200 hover:bg-brand-400/20 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {loading ? (
                <>
                  <svg className="h-5 w-5 animate-spin" viewBox="0 0 24 24" fill="none">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                  </svg>
                  Odesílám...
                </>
              ) : isCustomQuote ? (
                <>
                  Odeslat poptávku
                  <Icon name="send" size={18} />
                </>
              ) : (
                <>
                  Objednat
                  <Icon name="arrowRight" size={18} />
                </>
              )}
            </button>

            <p className="mt-3 text-center text-xs text-zinc-600">
              {isCustomQuote
                ? 'Poptávka je nezávazná.'
                : 'Po objednání budete přesměrováni na platbu.'}
            </p>
          </div>
        </div>
      </form>
    </div>
  );
}
