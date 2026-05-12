export interface Profile {
  id: string;
  email: string;
  full_name: string;
  phone: string | null;
  role: 'customer' | 'maker' | 'admin';
  avatar_url: string | null;
  created_at: string;
  updated_at: string;
}

export interface Maker {
  id: string;
  user_id: string;
  ico: string;
  dic: string | null;
  company_name: string;
  legal_form: string | null;
  street: string;
  city: string;
  zip: string;
  bio: string | null;
  website: string | null;
  bank_account: string;
  accepts_custom_orders: boolean;
  personal_pickup: boolean;
  pickup_address: string | null;
  pickup_note: string | null;
  is_verified: boolean;
  is_active: boolean;
  rating_avg: number;
  rating_count: number;
  total_orders: number;
  total_revenue: number;
  created_at: string;
  updated_at: string;
}

export interface Category {
  id: string;
  name: string;
  slug: string;
  icon: string | null;
  description: string | null;
  sort_order: number;
}

export interface Product {
  id: string;
  maker_id: string;
  category_id: string | null;
  title: string;
  description: string | null;
  price: number;
  price_type: 'fixed' | 'from' | 'on_request';
  images: string[];
  weight_grams: number | null;
  is_active: boolean;
  created_at: string;
  updated_at: string;
}

export interface MakerCategory {
  maker_id: string;
  category_id: string;
}

export interface MakerWithProfile extends Maker {
  profile?: Profile;
  categories?: Category[];
}

export interface ProductWithMaker extends Product {
  maker?: MakerWithProfile;
  category?: Category;
}
