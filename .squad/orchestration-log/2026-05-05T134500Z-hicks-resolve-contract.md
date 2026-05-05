| Field | Value |
|-------|-------|
| **Agent routed** | Hicks (Backend Dev) |
| **Why chosen** | User directive clarification: resolve endpoint contract must return 202 immediately with run_id; webhook dispatch needs run_id correlation. Hicks owns API contract and webhook dispatcher. |
| **Mode** | `sync` |
| **Why this mode** | Blocking decision: API contract changes are prerequisites for Ferro (frontend) and Bishop (orchestration). Multiple dependent work streams. |
| **Files authorized to read** | `.squad/decisions/inbox/copilot-directive-2026-05-05T134319-resolve-webhook.md`, `.squad/decisions/inbox/hicks-ticket-api-contract.md` (existing), Phase 2.5 architecture context. |
| **File(s) agent must produce** | Corrected `src/dotnet/AgenticResolution.Api/Api/TicketsEndpoints.cs` (resolve endpoint returns 202 with runId, statusUrl, eventsUrl). Updated `src/dotnet/AgenticResolution.Api/Webhooks/WebhookDispatchService.cs` (add run_id field to webhook payloads). Updated `src/dotnet/AgenticResolution.Api/appsettings.json` (set `Webhook:AutoDispatchOnTicketWrite` to false). Updated `.squad/decisions/inbox/hicks-resolve-webhook-contract.md` (contract specification). |
| **Outcome** | Completed — contract specified and implemented. Build succeeded. Webhook payload extended. Resolve endpoint fires webhook and returns immediately (202). |
