import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ProductForm } from '../_components/product-form';
import {
  createProduct,
  type MakerProductDetail,
  updateProduct,
} from '@/lib/api-client-helpers/maker-products';

/**
 * T-0174 (audit MAKER-M1/M2/M7): the product form's save feedback moves
 * to the shared SaveButton (dirty-tracked, in-viewport), a failed submit
 * brings the first errored field into view, and a successful create
 * hands off to the edit page with `?created=1`.
 */

const push = vi.fn();
const refresh = vi.fn();

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push, refresh }),
}));

vi.mock('@/lib/api-client-helpers/maker-products', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api-client-helpers/maker-products')>();
  return { ...actual, createProduct: vi.fn(), updateProduct: vi.fn() };
});

const createProductMock = vi.mocked(createProduct);
const updateProductMock = vi.mocked(updateProduct);

const CATEGORY_OPTIONS = [{ value: 'cat-1', label: '3D tisk' }];

const INITIAL: MakerProductDetail = {
  productId: 'p1',
  title: 'Držák',
  description: 'Popis',
  priceAmountMinor: 25000,
  priceCurrency: 'CZK',
  priceType: 'Fixed',
  fulfillmentType: 'MadeToOrder',
  weightGrams: 120,
  categoryId: 'cat-1',
  isActive: true,
  createdOn: '2026-08-01T00:00:00Z',
  images: [],
};

beforeEach(() => {
  vi.clearAllMocks();
  window.HTMLElement.prototype.scrollIntoView = vi.fn();
});

describe('ProductForm edit mode (SaveButton + dirty tracking)', () => {
  it('keeps the save button disabled until something changes', () => {
    render(<ProductForm mode="edit" initial={INITIAL} categoryOptions={CATEGORY_OPTIONS} />);

    const save = screen.getByRole('button', { name: 'Uložit změny' });
    expect(save).toBeDisabled();

    fireEvent.change(screen.getByDisplayValue('Držák'), { target: { value: 'Držák v2' } });
    expect(screen.getByRole('button', { name: 'Uložit změny' })).toBeEnabled();
  });

  it('confirms a successful save at the button and disarms once clean', async () => {
    updateProductMock.mockResolvedValue({ success: true, value: undefined } as Awaited<
      ReturnType<typeof updateProduct>
    >);
    render(<ProductForm mode="edit" initial={INITIAL} categoryOptions={CATEGORY_OPTIONS} />);

    fireEvent.change(screen.getByDisplayValue('Držák'), { target: { value: 'Držák v2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Uložit změny' }));

    // In-viewport confirmation lives on the button itself (SaveButton),
    // not in a top-of-form alert that scrolls out of sight.
    expect(await screen.findByRole('button', { name: /Uloženo/ })).toBeInTheDocument();
    expect(updateProductMock).toHaveBeenCalledTimes(1);
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('scrolls to and focuses the first errored field on a validation failure', async () => {
    updateProductMock.mockResolvedValue({
      success: false,
      error: {
        code: 'validation',
        message: '',
        type: 'Validation',
        fields: { Title: ['Název je povinný.'] },
      },
    } as Awaited<ReturnType<typeof updateProduct>>);
    render(<ProductForm mode="edit" initial={INITIAL} categoryOptions={CATEGORY_OPTIONS} />);

    fireEvent.change(screen.getByDisplayValue('Držák'), { target: { value: 'x' } });
    fireEvent.click(screen.getByRole('button', { name: 'Uložit změny' }));

    expect(await screen.findByText('Název je povinný.')).toBeInTheDocument();
    const title = document.getElementById('product-title');
    expect(title).not.toBeNull();
    expect(title!.scrollIntoView).toHaveBeenCalled();
    expect(document.activeElement).toBe(title);
  });
});

describe('ProductForm create mode', () => {
  it('hands off to the edit page with the created flag', async () => {
    createProductMock.mockResolvedValue({ success: true, value: { id: 'p-new' } } as Awaited<
      ReturnType<typeof createProduct>
    >);
    render(<ProductForm mode="create" categoryOptions={CATEGORY_OPTIONS} />);

    fireEvent.change(screen.getByLabelText(/Název/i), { target: { value: 'Nový produkt' } });
    fireEvent.click(screen.getByRole('button', { name: /Vytvořit/i }));

    await vi.waitFor(() => {
      expect(push).toHaveBeenCalledWith('/dashboard/maker/produkty/p-new?created=1');
    });
  });
});
