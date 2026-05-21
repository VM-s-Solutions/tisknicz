# Open questions for the user

> Append entries here when an agent needs a decision from the user that cannot be made internally. Reviewed at sprint checkpoints. Once answered, the decision moves into the relevant ADR / user story / ticket, and the entry is marked `answered`.

## Template

```
## Q-NNNN — <short title>
- **From:** <agent>
- **Ticket / context:** T-NNNN or "general"
- **Asked:** YYYY-MM-DD
- **Blocking:** yes | no
- **Question:** <one or two sentences>
- **Options the agent has considered:** <bullets, optional>
- **Status:** open | answered | obsolete
- **Answer (filled by user):**
```

---

## Q-0001 — Multi-user per maker account
- **From:** BA
- **Ticket / context:** general; arose during Batch 1 personas
- **Asked:** 2026-05-21
- **Blocking:** no
- **Question:** Workshops will eventually want multiple users per maker (owner + operators). When do we plan to enable this — post-MVP v1.1, or earlier?
- **Options the agent has considered:**
  - Defer to post-MVP v1.1 (default — keeps MVP simple, schema can accommodate via `maker_user` join table later)
  - Build now (adds permissions UI, invitation flow, audit trail on maker actions)
- **Status:** open
- **Answer (filled by user):**

## Q-0002 — Custom-quote flow for on-request products
- **From:** BA
- **Ticket / context:** general; trust-model decision deferred this
- **Asked:** 2026-05-21
- **Blocking:** no
- **Question:** Spec mentions `price_type = 'on_request'`. Without pre-purchase contact (locked in Batch 1), how should on-request products work at launch?
- **Options the agent has considered:**
  - Hide on-request products at launch — only `fixed` and `from` prices live in catalog
  - Show on-request products but the "Order" button submits a brief that creates an order in a new pre-existing state `quote_pending`; maker responds with a price; customer accepts → pays. Requires extra state in the order machine.
  - Allow on-request listings but disable the order CTA, instead show a generic "Coming soon — direct quotes" placeholder until post-MVP
- **Status:** open
- **Answer (filled by user):**

## Q-0003 — Admin assistant background
- **From:** BA
- **Ticket / context:** general; admin persona
- **Asked:** 2026-05-21
- **Blocking:** no
- **Question:** Is the admin assistant technical (developer-adjacent) or non-technical (operations / customer-support background)? Affects how much we invest in admin UX vs. CSV/SQL escape hatches.
- **Status:** open
- **Answer (filled by user):**
