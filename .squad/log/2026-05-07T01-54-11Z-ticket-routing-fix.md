# Session Log — Ticket Routing Fix

**Timestamp:** 2026-05-07T01:54:11Z

Ferro diagnosed Blazor ticket crash: configuration precedence issue in `TicketApiClient`. Updated `Program.cs` to prioritize `TICKETS_API_URL` over `ApiClient:BaseUrl`. Validated with local build and run.

Hicks verified endpoint routing: `/tickets` is UI route; API CRUD is `/api/tickets` on ca-api Container App. Set missing App Service settings. Hardened web client against HTML responses. Both builds pass.

**Status:** Complete — Ready for deployment verification.
