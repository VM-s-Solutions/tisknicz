import { RefreshButton } from '@/components/shared/refresh-button';
import { Alert } from '@/components/ui/alert';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import type { ApiError } from '@/lib/runtime/result';

/**
 * Failure surface for both profile pages (T-0173, audit CUST-M1).
 *
 * Both used to render `result.error.message` verbatim — against the
 * project's own rule that a raw backend message never reaches the UI
 * (`lib/runtime/errors.ts`) — with no retry and no login redirect, while
 * the orders page one route over handled all three correctly. The
 * Unauthorized case is now a redirect at the page level; everything else
 * lands here with translated copy and a real retry.
 */
export function ProfileLoadError({ error }: { readonly error: ApiError }) {
  return (
    <Alert variant="error">
      <div className="flex flex-col gap-3">
        <div>
          <p className="font-semibold">{t('dashboard.profile.load_error.title')}</p>
          <p className="mt-1 text-sm">{resolveErrorMessage(error)}</p>
        </div>
        <RefreshButton />
      </div>
    </Alert>
  );
}
