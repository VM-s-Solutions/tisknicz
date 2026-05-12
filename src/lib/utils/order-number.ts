export function generateOrderNumber(): string {
  const year = new Date().getFullYear();
  const random = Math.floor(Math.random() * 9000) + 1000;
  const seq = Date.now().toString().slice(-4);
  return `T-${year}${seq}${random}`;
}

export function generateInvoiceNumber(type: 'customer' | 'fee'): string {
  const prefix = type === 'customer' ? 'FV' : 'FP';
  const year = new Date().getFullYear();
  const seq = Date.now().toString().slice(-6);
  return `${prefix}-${year}${seq}`;
}
