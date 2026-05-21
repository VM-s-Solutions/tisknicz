# RLS audit checklist

SecOps runs this any time a migration adds or modifies a table.

## Per table

- [ ] RLS is enabled (`ALTER TABLE ... ENABLE ROW LEVEL SECURITY`)
- [ ] At least one SELECT policy exists for every role that should read
- [ ] At least one INSERT/UPDATE/DELETE policy for every role that should write
- [ ] Anonymous (no auth) cannot read or write anything sensitive
- [ ] Customer role can read only their own rows
- [ ] Maker role can read only their own maker's rows
- [ ] Admin role can read all
- [ ] Service role (server-side admin client) bypasses RLS — verified used only in webhooks/crons

## Test cases
- [ ] Logged-out user: every protected query returns 0 rows or 401
- [ ] Customer A cannot see customer B's orders
- [ ] Maker A cannot see maker B's orders or products
- [ ] Maker cannot update `platform_fee` or `maker_payout`
- [ ] Cross-tenant read (when multi-country lands) blocked by policy

## Multi-country note
Once country/tenancy lands, every policy must also scope by `country_id` or `tenant_id`. Add a second pass to this checklist when that ADR is accepted.
