import Link from 'next/link';
import { createServerClient } from '@/lib/supabase/server';

export async function Header() {
  let user = null;
  let profile: { role: string } | null = null;

  try {
    const supabase = await createServerClient();
    const { data } = await supabase.auth.getUser();
    user = data.user;

    if (user) {
      const { data: profileData } = await supabase
        .from('profiles')
        .select('role')
        .eq('id', user.id)
        .maybeSingle();
      profile = profileData;
    }
  } catch {
    // Supabase not configured — render as logged out
  }

  return (
    <header className="sticky top-0 z-50 border-b border-zinc-200/50 bg-white/80 backdrop-blur-xl">
      <div className="mx-auto flex h-16 max-w-7xl items-center justify-between px-4 sm:px-6 lg:px-8">
        <div className="flex items-center gap-8">
          <Link href="/" className="text-xl font-extrabold tracking-tight">
            <span className="text-gradient">Tiskni</span>
            <span className="text-zinc-900">.cz</span>
          </Link>
          <nav className="hidden items-center gap-1 md:flex">
            <NavLink href="/katalog">Katalog</NavLink>
            <NavLink href="/jak-to-funguje">Jak to funguje</NavLink>
            <NavLink href="/pro-tiskare">Pro tiskaře</NavLink>
          </nav>
        </div>

        <div className="flex items-center gap-3">
          {user ? (
            <Link
              href={
                profile?.role === 'admin'
                  ? '/dashboard/admin'
                  : profile?.role === 'maker'
                    ? '/dashboard/maker'
                    : '/dashboard/zakaznik'
              }
              className="rounded-xl bg-gradient-to-r from-brand-600 to-brand-500 px-5 py-2 text-sm font-semibold text-white shadow-md transition-all duration-200 hover:shadow-lg hover:scale-[1.02]"
            >
              Dashboard
            </Link>
          ) : (
            <>
              <Link
                href="/auth/login"
                className="rounded-xl px-4 py-2 text-sm font-medium text-zinc-600 transition-colors hover:text-zinc-900"
              >
                Přihlásit se
              </Link>
              <Link
                href="/auth/register"
                className="rounded-xl bg-gradient-to-r from-brand-600 to-brand-500 px-5 py-2 text-sm font-semibold text-white shadow-md transition-all duration-200 hover:shadow-lg hover:scale-[1.02]"
              >
                Registrace
              </Link>
            </>
          )}
        </div>
      </div>
    </header>
  );
}

function NavLink({ href, children }: { href: string; children: React.ReactNode }) {
  return (
    <Link
      href={href}
      className="group relative rounded-lg px-3 py-2 text-sm font-medium text-zinc-500 transition-colors hover:text-zinc-900"
    >
      {children}
      <span className="absolute inset-x-3 -bottom-px h-0.5 scale-x-0 rounded-full bg-brand-500 transition-transform duration-200 group-hover:scale-x-100" />
    </Link>
  );
}
