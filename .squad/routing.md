# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Architecture, scope, Azure resource design | Apone | "What resources do we need?", "Should this be Blazor Server or WASM?" |
| Blazor UI, components, pages, forms | Ferro | "Build the ticket entry form", "Add a status badge component" |
| .NET APIs, EF Core, SQL schema, webhooks | Hicks | "Create the Tickets API", "Wire up the webhook on save", "Add a migration" |
| Azure AI Search, Foundry Agents, Agent Framework | Bishop | "Design the search index", "Stand up the resolver agent" (Phase 2+) |
| Tests (unit, integration, bUnit) | Vasquez | "Write tests for the webhook trigger", "Add edge cases for ticket validation" |
| Setup documentation, runbooks, operator guides | Bob | "Document deployment", "Explain one-command setup", "Create setup runbook" |
| Code review | Apone | Review PRs, enforce reviewer-gate |
| Scope & priorities | Apone | What to build next, trade-offs |
| Session logging | Scribe | Automatic — never needs routing |
| Work monitoring / backlog | Ralph | "Ralph, go", "What's on the board?" |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Apone |
| `squad:apone` | Architecture / scoping issues | Apone |
| `squad:ferro` | Blazor / UI issues | Ferro |
| `squad:hicks` | API / DB / webhook issues | Hicks |
| `squad:bishop` | AI Search / Foundry Agents issues | Bishop |
| `squad:vasquez` | Test / quality issues | Vasquez |
| `squad:bob` | Documentation / setup guide issues | Bob |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, **Apone** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for Apone.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** While Hicks builds the API, spawn Vasquez to write test cases from the contract simultaneously.
7. **Bishop is on standby in Phase 1.** Do not route AI Search / Foundry work until the basic ticket→webhook path exists.
8. **No Azure deployment in Phase 1.** Local development only — Apone owns this rule.
