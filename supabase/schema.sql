-- ============================================
-- Tiskni.cz — Kompletní DB schéma
-- Spustit v Supabase SQL Editor
-- ============================================

-- 1. PROFILES
CREATE TABLE IF NOT EXISTS profiles (
  id UUID PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
  email TEXT NOT NULL,
  full_name TEXT NOT NULL,
  phone TEXT,
  role TEXT NOT NULL CHECK (role IN ('customer', 'maker', 'admin')) DEFAULT 'customer',
  avatar_url TEXT,
  created_at TIMESTAMPTZ DEFAULT NOW(),
  updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- 2. CATEGORIES
CREATE TABLE IF NOT EXISTS categories (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  name TEXT NOT NULL,
  slug TEXT NOT NULL UNIQUE,
  icon TEXT,
  description TEXT,
  sort_order INTEGER DEFAULT 0
);

-- 3. MAKERS
CREATE TABLE IF NOT EXISTS makers (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
  ico TEXT NOT NULL UNIQUE,
  dic TEXT,
  company_name TEXT NOT NULL,
  legal_form TEXT,
  street TEXT NOT NULL,
  city TEXT NOT NULL,
  zip TEXT NOT NULL,
  bio TEXT,
  website TEXT,
  bank_account TEXT NOT NULL,
  accepts_custom_orders BOOLEAN DEFAULT true,
  personal_pickup BOOLEAN DEFAULT false,
  pickup_address TEXT,
  pickup_note TEXT,
  is_verified BOOLEAN DEFAULT false,
  is_active BOOLEAN DEFAULT true,
  rating_avg DECIMAL(2,1) DEFAULT 0,
  rating_count INTEGER DEFAULT 0,
  total_orders INTEGER DEFAULT 0,
  total_revenue INTEGER DEFAULT 0,
  created_at TIMESTAMPTZ DEFAULT NOW(),
  updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- 4. MAKER CATEGORIES (M:N)
CREATE TABLE IF NOT EXISTS maker_categories (
  maker_id UUID REFERENCES makers(id) ON DELETE CASCADE,
  category_id UUID REFERENCES categories(id) ON DELETE CASCADE,
  PRIMARY KEY (maker_id, category_id)
);

-- 5. PRODUCTS
CREATE TABLE IF NOT EXISTS products (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  maker_id UUID NOT NULL REFERENCES makers(id) ON DELETE CASCADE,
  category_id UUID REFERENCES categories(id),
  title TEXT NOT NULL,
  description TEXT,
  price INTEGER NOT NULL,
  price_type TEXT NOT NULL CHECK (price_type IN ('fixed', 'from', 'on_request')) DEFAULT 'fixed',
  images TEXT[] DEFAULT '{}',
  weight_grams INTEGER,
  is_active BOOLEAN DEFAULT true,
  created_at TIMESTAMPTZ DEFAULT NOW(),
  updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- 6. PAYOUT BATCHES (must be before orders due to FK)
CREATE TABLE IF NOT EXISTS payout_batches (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  batch_number TEXT NOT NULL UNIQUE,
  status TEXT NOT NULL CHECK (status IN ('pending', 'processing', 'completed')) DEFAULT 'pending',
  total_amount INTEGER NOT NULL DEFAULT 0,
  order_count INTEGER NOT NULL DEFAULT 0,
  csv_url TEXT,
  processed_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

-- 7. ORDERS
CREATE TABLE IF NOT EXISTS orders (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  order_number TEXT NOT NULL UNIQUE,
  customer_id UUID NOT NULL REFERENCES profiles(id),
  maker_id UUID NOT NULL REFERENCES makers(id),
  product_id UUID REFERENCES products(id),
  title TEXT NOT NULL,
  description TEXT,
  quantity INTEGER DEFAULT 1,
  attachments TEXT[] DEFAULT '{}',
  product_price INTEGER NOT NULL,
  shipping_price INTEGER NOT NULL DEFAULT 0,
  platform_fee INTEGER NOT NULL,
  maker_payout INTEGER NOT NULL,
  total_price INTEGER NOT NULL,
  shipping_method TEXT NOT NULL CHECK (shipping_method IN ('zasilkovna', 'personal_pickup')),
  zasilkovna_branch_id TEXT,
  zasilkovna_branch_name TEXT,
  zasilkovna_packet_id TEXT,
  zasilkovna_tracking_url TEXT,
  customer_name TEXT NOT NULL,
  customer_email TEXT NOT NULL,
  customer_phone TEXT NOT NULL,
  status TEXT NOT NULL CHECK (status IN (
    'pending_payment', 'paid', 'accepted', 'shipped',
    'delivered', 'completed', 'cancelled', 'refunded', 'disputed'
  )) DEFAULT 'pending_payment',
  comgate_transaction_id TEXT,
  payment_method TEXT,
  paid_at TIMESTAMPTZ,
  payout_batch_id UUID REFERENCES payout_batches(id),
  paid_out_at TIMESTAMPTZ,
  accepted_at TIMESTAMPTZ,
  shipped_at TIMESTAMPTZ,
  delivered_at TIMESTAMPTZ,
  cancelled_at TIMESTAMPTZ,
  auto_deliver_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ DEFAULT NOW(),
  updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- 8. REVIEWS
CREATE TABLE IF NOT EXISTS reviews (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  order_id UUID NOT NULL REFERENCES orders(id) UNIQUE,
  customer_id UUID NOT NULL REFERENCES profiles(id),
  maker_id UUID NOT NULL REFERENCES makers(id),
  rating INTEGER NOT NULL CHECK (rating BETWEEN 1 AND 5),
  comment TEXT,
  maker_reply TEXT,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

-- 9. INVOICES
CREATE TABLE IF NOT EXISTS invoices (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  order_id UUID NOT NULL REFERENCES orders(id),
  invoice_number TEXT NOT NULL UNIQUE,
  type TEXT NOT NULL CHECK (type IN ('customer', 'fee')),
  issuer_name TEXT NOT NULL,
  issuer_ico TEXT,
  issuer_dic TEXT,
  issuer_address TEXT NOT NULL,
  recipient_name TEXT NOT NULL,
  recipient_ico TEXT,
  recipient_dic TEXT,
  recipient_address TEXT NOT NULL,
  amount_without_vat INTEGER NOT NULL,
  vat_rate INTEGER DEFAULT 0,
  vat_amount INTEGER DEFAULT 0,
  amount_with_vat INTEGER NOT NULL,
  description TEXT NOT NULL,
  issue_date DATE NOT NULL,
  due_date DATE NOT NULL,
  pdf_url TEXT,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

-- 10. MESSAGES
CREATE TABLE IF NOT EXISTS messages (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  order_id UUID NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
  sender_id UUID NOT NULL REFERENCES profiles(id),
  content TEXT NOT NULL,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================
-- INDEXES
-- ============================================
CREATE INDEX IF NOT EXISTS idx_makers_city ON makers(city);
CREATE INDEX IF NOT EXISTS idx_makers_active ON makers(is_active) WHERE is_active = true;
CREATE INDEX IF NOT EXISTS idx_products_maker ON products(maker_id);
CREATE INDEX IF NOT EXISTS idx_products_category ON products(category_id);
CREATE INDEX IF NOT EXISTS idx_products_active ON products(is_active) WHERE is_active = true;
CREATE INDEX IF NOT EXISTS idx_orders_customer ON orders(customer_id);
CREATE INDEX IF NOT EXISTS idx_orders_maker ON orders(maker_id);
CREATE INDEX IF NOT EXISTS idx_orders_status ON orders(status);
CREATE INDEX IF NOT EXISTS idx_reviews_maker ON reviews(maker_id);
CREATE INDEX IF NOT EXISTS idx_messages_order ON messages(order_id);

-- ============================================
-- RLS (Row Level Security)
-- ============================================
ALTER TABLE profiles ENABLE ROW LEVEL SECURITY;
ALTER TABLE makers ENABLE ROW LEVEL SECURITY;
ALTER TABLE categories ENABLE ROW LEVEL SECURITY;
ALTER TABLE maker_categories ENABLE ROW LEVEL SECURITY;
ALTER TABLE products ENABLE ROW LEVEL SECURITY;
ALTER TABLE orders ENABLE ROW LEVEL SECURITY;
ALTER TABLE reviews ENABLE ROW LEVEL SECURITY;
ALTER TABLE invoices ENABLE ROW LEVEL SECURITY;
ALTER TABLE messages ENABLE ROW LEVEL SECURITY;
ALTER TABLE payout_batches ENABLE ROW LEVEL SECURITY;

-- Profiles: users can read all, update own
CREATE POLICY "Profiles are viewable by everyone" ON profiles FOR SELECT USING (true);
CREATE POLICY "Users can update own profile" ON profiles FOR UPDATE USING (auth.uid() = id);
CREATE POLICY "Users can insert own profile" ON profiles FOR INSERT WITH CHECK (auth.uid() = id);

-- Categories: readable by everyone
CREATE POLICY "Categories are viewable by everyone" ON categories FOR SELECT USING (true);

-- Makers: readable by everyone, editable by owner
CREATE POLICY "Makers are viewable by everyone" ON makers FOR SELECT USING (true);
CREATE POLICY "Makers can update own profile" ON makers FOR UPDATE USING (auth.uid() = user_id);
CREATE POLICY "Users can register as maker" ON makers FOR INSERT WITH CHECK (auth.uid() = user_id);

-- Maker categories: readable by everyone
CREATE POLICY "Maker categories are viewable by everyone" ON maker_categories FOR SELECT USING (true);
CREATE POLICY "Makers can manage own categories" ON maker_categories FOR INSERT WITH CHECK (
  EXISTS (SELECT 1 FROM makers WHERE id = maker_id AND user_id = auth.uid())
);
CREATE POLICY "Makers can delete own categories" ON maker_categories FOR DELETE USING (
  EXISTS (SELECT 1 FROM makers WHERE id = maker_id AND user_id = auth.uid())
);

-- Products: readable by everyone, editable by owner maker
CREATE POLICY "Active products are viewable by everyone" ON products FOR SELECT USING (true);
CREATE POLICY "Makers can create own products" ON products FOR INSERT WITH CHECK (
  EXISTS (SELECT 1 FROM makers WHERE id = maker_id AND user_id = auth.uid())
);
CREATE POLICY "Makers can update own products" ON products FOR UPDATE USING (
  EXISTS (SELECT 1 FROM makers WHERE id = maker_id AND user_id = auth.uid())
);
CREATE POLICY "Makers can delete own products" ON products FOR DELETE USING (
  EXISTS (SELECT 1 FROM makers WHERE id = maker_id AND user_id = auth.uid())
);

-- Orders: visible to customer and maker involved
CREATE POLICY "Customers can view own orders" ON orders FOR SELECT USING (auth.uid() = customer_id);
CREATE POLICY "Makers can view their orders" ON orders FOR SELECT USING (
  EXISTS (SELECT 1 FROM makers WHERE id = maker_id AND user_id = auth.uid())
);
CREATE POLICY "Customers can create orders" ON orders FOR INSERT WITH CHECK (auth.uid() = customer_id);

-- Reviews: readable by everyone, creatable by customer
CREATE POLICY "Reviews are viewable by everyone" ON reviews FOR SELECT USING (true);
CREATE POLICY "Customers can create reviews" ON reviews FOR INSERT WITH CHECK (auth.uid() = customer_id);

-- Invoices: visible to involved parties
CREATE POLICY "Users can view own invoices" ON invoices FOR SELECT USING (
  EXISTS (SELECT 1 FROM orders WHERE id = order_id AND (customer_id = auth.uid() OR
    EXISTS (SELECT 1 FROM makers WHERE id = orders.maker_id AND user_id = auth.uid())
  ))
);

-- Messages: visible to order participants
CREATE POLICY "Order participants can view messages" ON messages FOR SELECT USING (
  EXISTS (SELECT 1 FROM orders WHERE id = order_id AND (customer_id = auth.uid() OR
    EXISTS (SELECT 1 FROM makers WHERE id = orders.maker_id AND user_id = auth.uid())
  ))
);
CREATE POLICY "Order participants can send messages" ON messages FOR INSERT WITH CHECK (
  EXISTS (SELECT 1 FROM orders WHERE id = order_id AND (customer_id = auth.uid() OR
    EXISTS (SELECT 1 FROM makers WHERE id = orders.maker_id AND user_id = auth.uid())
  ))
);

-- Payout batches: admin only (via service role), read by makers
CREATE POLICY "Makers can view payout batches" ON payout_batches FOR SELECT USING (true);

-- ============================================
-- SEED DATA — Kategorie
-- ============================================
INSERT INTO categories (name, slug, icon, description, sort_order) VALUES
  ('3D tisk', '3d-tisk', 'printer', 'FDM, SLA, resin tisk na zakázku', 1),
  ('Klasický tisk', 'klasicky-tisk', 'image', 'Vizitky, letáky, brožury, plakáty', 2),
  ('Potisk textilu', 'potisk-textilu', 'tshirt', 'DTF, DTG, sítotisk, sublimace', 3),
  ('Laser & CNC', 'laser-cnc', 'laser', 'Gravírování, řezání, frézování', 4),
  ('Velkoformát', 'velkoformat', 'frame', 'Bannery, rollupy, samolepky', 5),
  ('Handmade', 'handmade', 'palette', 'Originální výrobky, dekorace', 6)
ON CONFLICT (slug) DO NOTHING;

-- ============================================
-- TRIGGER: auto-create profile on signup
-- ============================================
CREATE OR REPLACE FUNCTION public.handle_new_user()
RETURNS trigger AS $$
BEGIN
  INSERT INTO public.profiles (id, email, full_name, role)
  VALUES (
    NEW.id,
    NEW.email,
    COALESCE(NEW.raw_user_meta_data->>'full_name', split_part(NEW.email, '@', 1)),
    COALESCE(NEW.raw_user_meta_data->>'role', 'customer')
  );
  RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

DROP TRIGGER IF EXISTS on_auth_user_created ON auth.users;
CREATE TRIGGER on_auth_user_created
  AFTER INSERT ON auth.users
  FOR EACH ROW EXECUTE FUNCTION public.handle_new_user();
