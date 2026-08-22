import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ImageManager } from '../_components/image-manager';
import {
  type ProductImageItem,
  uploadProductImage,
} from '@/lib/api-client-helpers/maker-products';

/**
 * T-0174 (audit MAKER-M6): the picker takes multiple files through a
 * sequential queue, the 10-image cap is visible BEFORE the 409 teaches
 * it, and files over remaining capacity are skipped with a notice.
 */

const refresh = vi.fn();

vi.mock('next/navigation', () => ({
  useRouter: () => ({ refresh }),
}));

vi.mock('@/lib/api-client-helpers/maker-products', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api-client-helpers/maker-products')>();
  return { ...actual, uploadProductImage: vi.fn(), removeProductImage: vi.fn() };
});

const uploadMock = vi.mocked(uploadProductImage);

function imageFixture(count: number): ProductImageItem[] {
  return Array.from({ length: count }, (_, i) => ({
    imageId: `img-${i}`,
    blobPath: `products/p1/${i}.jpg`,
    sortOrder: i,
  }));
}

function pickFiles(count: number): File[] {
  return Array.from(
    { length: count },
    (_, i) => new File(['x'], `foto-${i}.jpg`, { type: 'image/jpeg' }),
  );
}

function fileInput(): HTMLInputElement {
  const input = document.getElementById('product-image-upload');
  expect(input).not.toBeNull();
  return input as HTMLInputElement;
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('ImageManager', () => {
  it('shows the counter and disables upload at the 10-image cap', () => {
    render(<ImageManager productId="p1" images={imageFixture(10)} />);

    expect(screen.getByText('Fotky: 10/10')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Nahrát/i })).toBeDisabled();
    expect(fileInput()).toBeDisabled();
  });

  it('uploads picked files sequentially and refreshes once', async () => {
    uploadMock.mockResolvedValue({ success: true, value: { imageId: 'new' } } as Awaited<
      ReturnType<typeof uploadProductImage>
    >);
    render(<ImageManager productId="p1" images={imageFixture(0)} />);

    fireEvent.change(fileInput(), { target: { files: pickFiles(3) } });

    await vi.waitFor(() => {
      expect(uploadMock).toHaveBeenCalledTimes(3);
    });
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('skips files over remaining capacity and says so', async () => {
    uploadMock.mockResolvedValue({ success: true, value: { imageId: 'new' } } as Awaited<
      ReturnType<typeof uploadProductImage>
    >);
    render(<ImageManager productId="p1" images={imageFixture(9)} />);

    fireEvent.change(fileInput(), { target: { files: pickFiles(2) } });

    expect(await screen.findByText(/nebyly nahrány/)).toBeInTheDocument();
    expect(uploadMock).toHaveBeenCalledTimes(1);
  });

  it('lists each failed file by name and keeps successes', async () => {
    uploadMock
      .mockResolvedValueOnce({ success: true, value: { imageId: 'new' } } as Awaited<
        ReturnType<typeof uploadProductImage>
      >)
      .mockResolvedValueOnce({
        success: false,
        error: { code: 'file.tooLarge', message: '', type: 'Validation' },
      } as Awaited<ReturnType<typeof uploadProductImage>>);
    render(<ImageManager productId="p1" images={imageFixture(0)} />);

    fireEvent.change(fileInput(), { target: { files: pickFiles(2) } });

    expect(await screen.findByText(/foto-1\.jpg/)).toBeInTheDocument();
    expect(refresh).toHaveBeenCalledTimes(1);
  });
});
