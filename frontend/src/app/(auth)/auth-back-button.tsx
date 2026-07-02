'use client';

import { useRouter } from 'next/navigation';
import { Button } from '@/components/ui/button';
import { t } from '@/lib/i18n';

/**
 * Navigates to previous history entry when available, otherwise falls
 * back to home so users never get stuck on direct-link auth pages.
 */
export function AuthBackButton() {
  const router = useRouter();

  function handleBack() {
    if (window.history.length > 1) {
      router.back();
      return;
    }

    router.push('/');
  }

  return (
    <Button
      type="button"
      variant="ghost"
      size="sm"
      onClick={handleBack}
      className="w-fit px-1 text-sky-400 hover:bg-transparent hover:text-sky-300"
    >
      <span aria-hidden="true">←</span>
      <span>{t('common.back')}</span>
    </Button>
  );
}