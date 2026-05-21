# Communication rules

## Core principle: artifacts, not chat

Agents do not "talk" to each other. They produce and consume **versioned files** in this repo. Everything is in Git, reviewable, and reproducible.

## Channels

| Need | Channel |
|---|---|
| Hand off work between agents | Ticket status update in `docs/tickets/T-NNNN-*.md` |
| Record a design decision | New ADR in `docs/adr/NNNN-*.md` |
| Capture a user requirement | User story in `docs/user-stories/<persona>/*.md` |
| Report progress to the user | Sprint status update in `docs/status/sprint-N.md` |
| Ask the user a clarifying question | Append entry to `docs/questions/open.md` |
| Review someone's work | PR comments on the GitHub feature branch |
| Report a defect | Append to QA section of the ticket; PM may spawn a new ticket |

## Escalation paths

- BA → user (via `questions/open.md`) when a user story has ambiguity that blocks AC
- Architect → user (via `questions/open.md`) when a decision has lasting impact (money, security, vendor lock-in)
- BE/FE/DB → Architect when the design as specified doesn't fit reality
- Reviewer → Architect on design concerns; → SecOps on security concerns
- SecOps → user for production secrets, vendor account setup
- PM → user only at sprint checkpoints, or when a ticket is fully blocked

**Never** escalate mid-ticket if the question can wait for the next checkpoint. Batch by default.

## The `questions/open.md` file

Format per entry:

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

When the user answers, the asker (or PM) closes the entry and moves the decision into the relevant ADR, user story, or ticket. The `questions/open.md` file is not the system of record — it's the inbox.

## What agents must NOT do

- Make user-visible product decisions unilaterally (always escalate)
- Modify ADRs that are in `accepted` status (write a new ADR that supersedes)
- Skip the ticket lifecycle ("just a small fix" still needs a ticket if it affects shared code)
- Reference verbal context — if it's not in a file, it didn't happen
