'use client';

import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { startGoogleOAuth } from '@/lib/api-client-helpers/auth';
import { t } from '@/lib/i18n';
import { type ApiHost } from '@/lib/runtime/api-fetch';

interface GoogleSignInButtonProps {
  /** Which .NET host to start the flow against — login/register pages are customer-only for the MVP. */
  host: ApiHost;
  /** Bubbles the localized failure message up so the caller can render it in its shared error slot (Alert). */
  onError: (message: string) => void;
}

/**
 * "Sign in with Google" trigger (T-0026). The smallest possible client
 * boundary — it only calls {@link startGoogleOAuth} and redirects the
 * browser to Google's authorization URL on success. Google GET-redirects
 * the result straight back to the backend's `google/callback` route;
 * this component never sees the callback.
 */
export function GoogleSignInButton({ host, onError }: GoogleSignInButtonProps) {
  const [submitting, setSubmitting] = useState(false);

  async function handleClick() {
    setSubmitting(true);
    const result = await startGoogleOAuth(host);

    if (result.success) {
      window.location.href = result.value.authorizationUrl;
      return;
    }

    setSubmitting(false);
    onError(mapStartGoogleOAuthError(result.error.code));
  }

  return (
    <Button type="button" variant="secondary" loading={submitting} onClick={handleClick} className="w-full">
      <Icon name="google" size={18} />
      {submitting ? t('auth.login.submitting') : t('auth.google.signInButton')}
    </Button>
  );
}

function mapStartGoogleOAuthError(code: string): string {
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
      return t('auth.google.startFailed');
  }
}
