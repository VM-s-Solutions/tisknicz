import { PLATFORM_FEE_RATE } from '@/lib/constants';

interface OrderPricing {
  product_price: number;
  shipping_price: number;
  platform_fee: number;
  maker_payout: number;
  total_price: number;
}

export function calculateOrderPricing(productPrice: number, shippingPrice: number): OrderPricing {
  const platformFee = Math.round(productPrice * PLATFORM_FEE_RATE);
  const makerPayout = productPrice - platformFee + shippingPrice;
  const totalPrice = productPrice + shippingPrice;

  return {
    product_price: productPrice,
    shipping_price: shippingPrice,
    platform_fee: platformFee,
    maker_payout: makerPayout,
    total_price: totalPrice,
  };
}

export function formatCurrency(amount: number): string {
  return new Intl.NumberFormat('cs-CZ', {
    style: 'decimal',
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  }).format(amount) + ' Kč';
}
