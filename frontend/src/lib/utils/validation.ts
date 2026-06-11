export function validateICO(ico: string): boolean {
  if (!/^\d{8}$/.test(ico)) return false;

  const weights = [8, 7, 6, 5, 4, 3, 2];
  let sum = 0;
  for (let i = 0; i < 7; i++) {
    sum += parseInt(ico[i], 10) * weights[i];
  }
  const remainder = sum % 11;
  let checkDigit: number;
  if (remainder === 0) checkDigit = 1;
  else if (remainder === 1) checkDigit = 0;
  else checkDigit = 11 - remainder;

  return parseInt(ico[7], 10) === checkDigit;
}

export function validateBankAccount(account: string): boolean {
  return /^(\d{1,6}-)?\d{2,10}\/\d{4}$/.test(account);
}

export function validatePhone(phone: string): boolean {
  return /^(\+420)?\s?\d{3}\s?\d{3}\s?\d{3}$/.test(phone.trim());
}

// ---- Checkout mirrors (T-0084a) ----
// UX pre-checks ONLY — the backend stays authoritative; server-side
// rejections render via ApiError.fields (patterns.md B.17).

/** Mirror of T-0063 CreateOrder.Validator CzechPhoneRegex (backend authoritative — UX pre-check only). */
export const CZECH_PHONE_PATTERN = /^(\+420\s?)?[6-9]\d{2}\s?\d{3}\s?\d{3}$/;
/** Mirror of T-0063 CreateOrder.Validator CustomerName MinimumLength(2). */
export const ORDER_CONTACT_NAME_MIN = 2;
/** Mirror of T-0063 CreateOrder.Validator CustomerName MaximumLength(100). */
export const ORDER_CONTACT_NAME_MAX = 100;
/** Mirror of T-0063 CreateOrder.Validator CustomerEmail MaximumLength(254). */
export const ORDER_CONTACT_EMAIL_MAX = 254;
/** Mirror of T-0063 CreateOrder.Validator CustomerNotes MaximumLength(2000). */
export const ORDER_NOTES_MAX = 2000;
/** Mirror of T-0064 Order.MaxAttachmentCount (10 attachments per order). */
export const ORDER_ATTACHMENT_MAX_FILES = 10;
/** Mirror of T-0064 OrderAttachmentValidator max size (10 MiB per file). */
export const ORDER_ATTACHMENT_MAX_BYTES = 10 * 1024 * 1024;
/** Mirror of T-0064 OrderAttachmentValidator allowed content types. */
export const ORDER_ATTACHMENT_ALLOWED_TYPES = new Set([
  'application/pdf',
  'image/jpeg',
  'image/png',
  'image/webp',
]);
