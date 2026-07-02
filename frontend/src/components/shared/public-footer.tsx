import Link from 'next/link';
import { t } from '@/lib/i18n';

const CUSTOMER_LINKS = [
  { href: '/katalog', key: 'footer.link.catalog' as const },
  { href: '/jak-to-funguje', key: 'footer.link.how_it_works' as const },
];

const MAKER_LINKS = [
  { href: '/pro-makery', key: 'footer.link.for_makers' as const },
  { href: '/register?type=maker', key: 'footer.link.maker_registration' as const },
];

const INFO_LINKS = [
  { href: '/vop', key: 'footer.link.terms' as const },
  { href: '/gdpr', key: 'footer.link.privacy' as const },
];

function FooterColumn({
  title,
  links,
}: {
  title: string;
  links: ReadonlyArray<{ href: string; key: Parameters<typeof t>[0] }>;
}) {
  return (
    <div className="space-y-3">
      <h3 className="text-sm font-semibold uppercase tracking-wide text-zinc-400">{title}</h3>
      <ul className="space-y-2">
        {links.map((link) => (
          <li key={link.href}>
            <Link href={link.href} className="text-sm text-zinc-300 transition-colors hover:text-white">
              {t(link.key)}
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}

export function PublicFooter() {
  const year = new Date().getFullYear();

  return (
    <footer className="mt-16 border-t border-zinc-800 bg-surface-secondary">
      <div className="mx-auto grid max-w-7xl gap-10 px-4 py-12 sm:grid-cols-3 sm:px-6 lg:px-8">
        <FooterColumn title={t('footer.customers')} links={CUSTOMER_LINKS} />
        <FooterColumn title={t('footer.makers')} links={MAKER_LINKS} />
        <FooterColumn title={t('footer.information')} links={INFO_LINKS} />
      </div>
      <div className="border-t border-zinc-800 px-4 py-4 sm:px-6 lg:px-8">
        <p className="mx-auto max-w-7xl text-sm text-zinc-500">{t('footer.copyright', { year })}</p>
      </div>
    </footer>
  );
}
