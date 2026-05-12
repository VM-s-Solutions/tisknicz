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
