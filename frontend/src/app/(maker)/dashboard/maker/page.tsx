import { redirect } from 'next/navigation';

/**
 * `/dashboard/maker` had no page at all (T-0173, audit MAKER-L1): logins
 * are routed to `/objednavky` by `audienceHome`, but a maker who typed or
 * bookmarked the bare dashboard URL got a 404 inside their own dashboard
 * chrome. Send them to the same landing the login flow uses.
 */
export default function MakerDashboardIndex(): never {
  redirect('/dashboard/maker/objednavky');
}
