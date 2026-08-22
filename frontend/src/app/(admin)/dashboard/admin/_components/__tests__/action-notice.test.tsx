import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ActionNoticeProvider, useActionNotice } from '../action-notice';

/**
 * T-0176 (audit ADM-M5): outbox retry/acknowledge rendered their success
 * message INSIDE the row, and both actions remove the event from the
 * stalled set — so the refresh unmounted the row and its confirmation
 * before it could be read. The notice must outlive the row.
 */
function Row({ label }: { readonly label: string }) {
  const notice = useActionNotice();
  return (
    <button type="button" onClick={() => notice.report(`${label} hotovo`)}>
      {label}
    </button>
  );
}

describe('ActionNoticeProvider', () => {
  it('keeps the notice after the reporting row unmounts', () => {
    // Mirrors the real flow: the provider sits above the list and stays
    // mounted while the acted-on row disappears from the stalled set.
    function Harness({ showRow }: { readonly showRow: boolean }) {
      return (
        <ActionNoticeProvider>{showRow ? <Row label="retry" /> : <p>prázdné</p>}</ActionNoticeProvider>
      );
    }
    const { rerender } = render(<Harness showRow />);

    fireEvent.click(screen.getByRole('button', { name: 'retry' }));
    expect(screen.getByText('retry hotovo')).toBeInTheDocument();

    rerender(<Harness showRow={false} />);

    expect(screen.queryByRole('button', { name: 'retry' })).not.toBeInTheDocument();
    expect(screen.getByText('retry hotovo')).toBeInTheDocument();
  });

  it('is a no-op outside a provider so rows still render standalone', () => {
    render(<Row label="retry" />);
    expect(() => fireEvent.click(screen.getByRole('button', { name: 'retry' }))).not.toThrow();
  });
});
