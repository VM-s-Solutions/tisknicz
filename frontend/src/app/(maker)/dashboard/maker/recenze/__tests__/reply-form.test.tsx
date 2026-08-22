import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ReplyForm } from '../reply-form';
import { respondToReview } from '@/lib/api-client-helpers/reviews-client';

/**
 * T-0174 (audit MAKER-H1): the success path used to leave
 * `submitting`/`inFlightRef` armed forever — `router.refresh()` does not
 * remount the island, so a successful reply bricked the form. These
 * tests pin the re-enable, plus the MAKER-L6 collapse-behind-"Upravit"
 * behavior for existing replies.
 */

const refresh = vi.fn();

vi.mock('next/navigation', () => ({
  useRouter: () => ({ refresh }),
}));

vi.mock('@/lib/api-client-helpers/reviews-client', () => ({
  respondToReview: vi.fn(),
}));

vi.mock('@/lib/runtime/errors', () => ({
  resolveErrorMessage: () => 'Odpověď se nepodařilo uložit.',
}));

const respondToReviewMock = vi.mocked(respondToReview);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('ReplyForm', () => {
  it('re-enables and collapses after a successful submit', async () => {
    respondToReviewMock.mockResolvedValue({ success: true, value: undefined } as Awaited<
      ReturnType<typeof respondToReview>
    >);
    render(<ReplyForm reviewId="r1" />);

    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Děkujeme!' } });
    fireEvent.click(screen.getByRole('button', { name: /odeslat/i }));

    // The regression: the form stayed on "Odesílám…" forever. Now it
    // collapses to the edit affordance and stays interactive.
    const editButton = await screen.findByRole('button', { name: 'Upravit odpověď' });
    expect(editButton).toBeEnabled();
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('renders collapsed with an edit affordance when a reply already exists', () => {
    render(<ReplyForm reviewId="r1" initialReply="Původní odpověď" />);

    expect(screen.getByRole('button', { name: 'Upravit odpověď' })).toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });

  it('opens prefilled on edit and cancel restores the collapsed state', () => {
    render(<ReplyForm reviewId="r1" initialReply="Původní odpověď" />);

    fireEvent.click(screen.getByRole('button', { name: 'Upravit odpověď' }));
    expect(screen.getByRole('textbox')).toHaveValue('Původní odpověď');

    fireEvent.click(screen.getByRole('button', { name: 'Zrušit' }));
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(respondToReviewMock).not.toHaveBeenCalled();
  });

  it('shows the error and re-enables the form on failure', async () => {
    respondToReviewMock.mockResolvedValue({
      success: false,
      error: { code: 'x', message: '', type: 'Transient' },
    } as Awaited<ReturnType<typeof respondToReview>>);
    render(<ReplyForm reviewId="r1" />);

    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Děkujeme!' } });
    fireEvent.click(screen.getByRole('button', { name: /odeslat/i }));

    expect(await screen.findByText('Odpověď se nepodařilo uložit.')).toBeInTheDocument();
    expect(screen.getByRole('textbox')).toBeEnabled();
    expect(refresh).not.toHaveBeenCalled();
  });
});
