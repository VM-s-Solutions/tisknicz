'use client';

import { useRouter } from 'next/navigation';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
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
      className="mb-4 w-fit px-1 text-brand-300 hover:text-brand-200"
    >
      <span aria-hidden="true">
        <Icon name="arrowLeft" size={16} />
      </span>
      <span>{t('common.back')}</span>
    </Button>
  );
}