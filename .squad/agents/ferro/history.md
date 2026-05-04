# Project Context

- **Owner:** Jason Farrell
- **Project:** agentic-resolution — Blazor frontend simulating a ServiceNow ticket entry experience. Submits tickets that persist to SQL Server and trigger a webhook, eventually routed through AI Search + Foundry Agents.
- **Stack:** Blazor (.NET), Azure App Service (target host), Bootstrap or Fluent UI candidate
- **Phase 1 scope:** ServiceNow-like ticket entry UI. No Azure deployment yet.
- **Created:** 2026-04-29

## Phase 1 Architecture (Apone)

**Solution:** Single-project Blazor Server with embedded minimal API endpoints (no separate API project).
- UI lives in `/src/AgenticResolution.Web/Pages/` (Razor components for ticket form, list, detail)
- Shared Blazor components in `Components/`
- POST /api/tickets endpoint in `Endpoints/TicketEndpoints.cs`
- WebhookService handles async dispatch post-save

**Affected:** Your ticket form UI submits to POST /api/tickets. Hicks handles the endpoint. Design components for: ticket #, short description, category (enum: Hardware/Software/Network/Access), priority (1-4), caller name. State is auto-set to "New".

**Questions pending:** Ticket number format? Enum vs free-text categories? .NET 9 or 8?

## Learnings

- **2026-04-29 — Phase 1 UI shipped.** Single-project Blazor Server (net10.0) at `src/AgenticResolution.Web/`. Built clean (0 warnings).
  - **Routes:**
    - `/` → redirects to `/tickets` (`Pages/Index.razor`)
    - `/tickets` — list view (`Pages/Tickets/Index.razor`) — table: Number / Short description / Priority / State / Caller / Created. Page size **25**, prev/next pagination.
    - `/tickets/new` — entry form (`Pages/Tickets/New.razor`)
    - `/tickets/{number}` — read-only detail (`Pages/Tickets/Detail.razor`)
  - **Components:** `Components/Layout/{MainLayout,NavMenu}.razor`, `Components/Tickets/{TicketForm,PriorityBadge,StateBadge}.razor`, `Components/{App,Routes,_Imports}.razor`.
  - **API client:** `Services/TicketsApiClient.cs` — typed HttpClient registered in `Program.cs` with `AddHttpClient<TicketsApiClient>()`, BaseAddress from `ApiBaseUrl` config (default `http://localhost:5000/`). Wraps `ListAsync(page,pageSize)`, `GetAsync(number)`, `CreateAsync(req)` returning `ApiResult<T>`.
  - **DTOs agreed with Hicks (`Models/Dtos/`):** `CreateTicketRequest` (Caller, ShortDescription, Description, Category free-text, Priority enum, AssignedTo), `TicketResponse` (full record incl. `TicketPriority`/`TicketState` enums), `TicketListResponse` (Items, Page, PageSize, **Total** — matches Hicks's `PagedResponse<T>` shape, not `TotalCount`), `ValidationProblemResponse` (ProblemDetails-shaped).
  - **Enum coordination with Hicks:** my client uses Hicks's `AgenticResolution.Web.Models.{TicketPriority,TicketState}` directly. Note priority enum ordering: **Low=1, Moderate=2, High=3, Critical=4** (Hicks's choice — opposite of ServiceNow numeric). UI labels still read "1-Critical … 4-Low" via display-label mapping in `PriorityBadge`/form dropdown.
  - **Program.cs:** Hicks merged my services + pipeline cleanly under banner comments — no conflicts. He added EF Core, App Insights, Key Vault, webhook hosted service.
  - **Styling:** Bootstrap 5.3.3 via jsDelivr CDN (App.razor `<head>`); custom palette in `wwwroot/app.css` (slate top bar, gray sidebar, professional/utilitarian — not a SN clone).
  - **Out of scope honored:** no local-dev README, no SQL Docker UI, no webhook test page.
  - **Decision drop:** `.squad/decisions/inbox/ferro-ticket-ui-choices.md`.

---

## 2026-04-29 — DTO alignment + priority remap removal

**Trigger:** Decisions drop — align to Hicks's `PagedResponse<T>.Total`; ServiceNow enum (Critical=1…Low=4) is now canonical, drop UI remap.

### DTO alignment
- `Models/Dtos/TicketListResponse.cs` already exposed `Total` (no `TotalCount` rename needed — earlier work was already aligned to Hicks's wire shape). Verified `Pages/Tickets/Index.razor` and `Services/TicketsApiClient.cs` reference `Total` / `TotalPages` correctly. Kept the local mirror DTO instead of taking a project reference to Hicks's `PagedResponse<T>`; JSON deserializes by shape and we avoid coupling Web to the API project.

### Priority remap removal
- `Components/Tickets/PriorityBadge.razor`: replaced the per-value `Label` switch (which hardcoded `"1 - Critical"`…`"4 - Low"` to compensate for the old reversed enum) with `$"{(int)Priority} - {Priority}"`. Once Hicks flips the enum to Critical=1, the int prefix renders correctly with zero remap.
- `Components/Tickets/TicketForm.razor`: replaced four hardcoded `<option>` lines with a `@foreach` over `{ Critical, High, Moderate, Low }` rendering `@((int)p) - @p`. Same dynamic story.
- Color palette refreshed to a warm→cool severity gradient — see decision drop `ferro-priority-palette.md`.

### Build
- `dotnet build` from repo root: **succeeded, 0 warnings, 0 errors**.
- Note: the local `Models/Ticket.cs` enum in this Web project still shows `Low=1…Critical=4` — that's Hicks's territory (he owns the canonical enum). My code references enum members by name, so display correctness depends on the upstream flip landing. Build passes either way; UI text only renders correctly post-flip.
