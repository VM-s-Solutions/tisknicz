import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PasswordInput } from '../password-input';

/**
 * The reveal toggle is the only stateful thing this primitive owns, so
 * that is what is tested: the input `type` actually flips, the button
 * announces what it will do next, and a disabled field cannot be peeked.
 */
describe('PasswordInput', () => {
  it('starts masked and exposes a "show" toggle', () => {
    render(<PasswordInput label="Heslo" defaultValue="tajneheslo" />);

    expect(screen.getByLabelText('Heslo')).toHaveAttribute('type', 'password');
    const toggle = screen.getByRole('button', { name: 'Zobrazit heslo' });
    expect(toggle).toHaveAttribute('aria-pressed', 'false');
  });

  it('reveals the value on click and flips back on the second click', () => {
    render(<PasswordInput label="Heslo" defaultValue="tajneheslo" />);

    fireEvent.click(screen.getByRole('button', { name: 'Zobrazit heslo' }));
    expect(screen.getByLabelText('Heslo')).toHaveAttribute('type', 'text');
    const toggle = screen.getByRole('button', { name: 'Skrýt heslo' });
    expect(toggle).toHaveAttribute('aria-pressed', 'true');

    fireEvent.click(toggle);
    expect(screen.getByLabelText('Heslo')).toHaveAttribute('type', 'password');
    expect(screen.getByRole('button', { name: 'Zobrazit heslo' })).toBeInTheDocument();
  });

  it('points the toggle at the field it controls', () => {
    render(<PasswordInput label="Heslo znovu" defaultValue="x" />);

    const input = screen.getByLabelText('Heslo znovu');
    expect(screen.getByRole('button', { name: 'Zobrazit heslo' })).toHaveAttribute(
      'aria-controls',
      input.id,
    );
  });

  it('disables the toggle together with the field', () => {
    render(<PasswordInput label="Heslo" defaultValue="x" disabled />);

    expect(screen.getByRole('button', { name: 'Zobrazit heslo' })).toBeDisabled();
  });

  it('renders an inline error like any other field', () => {
    render(<PasswordInput label="Heslo znovu" error="Hesla se neshodují." />);

    expect(screen.getByText('Hesla se neshodují.')).toBeInTheDocument();
  });
});
