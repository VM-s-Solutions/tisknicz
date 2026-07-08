'use client';

import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { startAppleOAuth } from '@/lib/api-client-helpers/auth';
import { t } from '@/lib/i18n';
import { type ApiHost } from '@/lib/runtime/api-fetch';

interface AppleSignInButtonProps {
  /** Which .NET host to start the flow against — login/register pages are customer-only for the MVP. */
  host: ApiHost;
  /** Bubbles the localized failure message up so the caller can render it in its shared error slot (Alert). */
  onError: (message: string) => void;
}

/**
 * "Sign in with Apple" trigger (T-0139, AC-10). Kept as the smallest
 * possible client boundary: it only calls {@link startAppleOAuth} and
 * redirects the browser to Apple's authorization URL on success. Apple
 * itself POSTs the result straight back to the backend's
 * `apple/callback` route (`response_mode=form_post`) — this component
 * never sees the callback.
 */
export function AppleSignInButton({ host, onError }: AppleSignInButtonProps) {
  const [submitting, setSubmitting] = useState(false);

  async function handleClick() {
    setSubmitting(true);
    const result = await startAppleOAuth(host);

    if (result.success) {
      window.location.href = result.value.authorizationUrl;
      return;
    }

    setSubmitting(false);
    onError(mapStartAppleOAuthError(result.error.code));
  }

  return (
    <Button type="button" variant="secondary" loading={submitting} onClick={handleClick} className="w-full">
      <Icon name="apple" size={18} />
      {submitting ? t('auth.login.submitting') : t('auth.apple.signInButton')}
    </Button>
  );
}

function mapStartAppleOAuthError(code: string): string {
  switch (code) {
    case 'auth.oauthNotAllowedForAdmin':
      return t('auth.oauthNotAllowedForAdmin');
    case 'auth.oauthInvalidState':
      return t('auth.oauthInvalidState');
    case 'auth.oauthEmailNotVerified':
      return t('auth.oauthEmailNotVerified');
    case 'auth.oauthExchangeFailed':
      return t('auth.oauthExchangeFailed');
    default:
      return t('auth.apple.startFailed');
  }
}
