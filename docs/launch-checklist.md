# Makables — Launch checklist

Blocking pre-launch action items. Each line is gated; go-live is blocked until
every BLOCKING item is resolved. Maintained alongside the tickets that surface
the gap (the ticket scaffolds the route/feature; the line tracks the missing
input that only the operator can supply).

## Legal

- [ ] **Legal text (Q-0030, BLOCKING):** JVM YORE s.r.o. must supply approved
  VOP (obchodní podmínky) + GDPR privacy/cookie text. Pages `/vop` + `/gdpr` are
  scaffolded (shell + nav-reachable route + i18n keys + a visible placeholder
  banner) by T-0130; only the legal TEXT is missing. Before go-live: replace the
  `static.legal_placeholder.banner` Alert and populate the `static.terms.*` /
  `static.privacy.*` keys with the approved text. See `docs/questions/open.md`
  Q-0030 (incl. the open sub-question on a cookie-consent banner / cookie
  management UI — confirm whether launch needs one).
