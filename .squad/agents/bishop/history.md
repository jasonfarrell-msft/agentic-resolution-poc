# Project Context

- **Owner:** Jason Farrell
- **Project:** agentic-resolution — Phase 2+ work focuses on Azure AI Search and Foundry Agents (with Agent Framework) consuming the webhook fired on ticket save, then either auto-resolving or escalating/assigning the ticket.
- **Stack:** Azure AI Search, Azure AI Foundry Agents, Microsoft Agent Framework, .NET
- **Phase 1 scope:** standby — no AI work until ticket→DB→webhook path is in place.
- **Created:** 2026-04-29

## Core Context

**Current state (2026-05-05):**
- Phase 2 AI pipeline architecture finalized: specialized decomposers (IncidentDecomposer + RequestDecomposer) for type-specific KB retrieval
- Question-driven resolution pipeline: Classifier → Incident/RequestAgent (fetch) → DecomposerAgent (KB search) → EvaluatorAgent → Resolution/Escalation
- Hosted agents in Container Apps with Foundry `/invocations` protocol; MCP server for ticket operations
- Azure AI Search index `tickets-index` ready (14 fields, hybrid BM25+vector+semantic, text-embedding-3-small 1536d)
- No blocking schema questions; standby on Hicks's gates G1–G7

**Key locked decisions:**
- Hybrid search: BM25 + vector similarity + semantic reranking; top 5 results to triage agent
- Single index, no multi-index KB corpus (Phase 3+)
- Incident vs Request dichotomy preserved through decomposition
- Pre-fetch search results before agent eval (no tool-calling latency)
- Seed 25 pre-resolved IT scenarios at gate G7

---

## Historical Summary

See `bishop-history-archive-2026-05-04.md` for detailed chronology (2026-04-29 through 2026-05-04):
- Phase 2 kickoff & search index schema finalization (2026-04-29)
- Phase 1 scaffold complete (2026-04-29)
- Foundry Agent wiring & hosted agents migration (2026-04-30)
- Question-driven resolution pipeline design (2026-05-04)
- DecomposerAgent split into IncidentDecomposer + RequestDecomposer (2026-05-04 decision)
