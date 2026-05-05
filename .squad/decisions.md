# Squad Decisions

### 2026-05-05: Bishop — Split DecomposerAgent into IncidentDecomposer + RequestDecomposer

**By:** Bishop (AI/Agents Specialist)  
**Requested by:** Jason Farrell

**Status:** Implemented and validated.

**Context:** The `DecomposerAgent` was a single agent performing question-driven KB retrieval for all ticket types. Incident tickets require **failure-mode thinking** (root cause, scope, recovery); service request tickets require **fulfillment thinking** (prerequisites, procedure, approval). A single SYSTEM_PROMPT cannot simultaneously frame both mindsets at the level of precision needed for accurate KB retrieval.

**Decision:** Split into two specialized agents with distinct SYSTEM_PROMPTs, question archetypes, and KB search strategies:

- **IncidentDecomposer** (`src/agents_py/agents/incident_decomposer/__init__.py`): Diagnostic framing with question archetypes (ROOT CAUSE, SCOPE, RECOVERY, VALIDATION) and search strategy (symptom patterns, component/service name, error codes, troubleshooting KB tags)
- **RequestDecomposer** (`src/agents_py/agents/request_decomposer/__init__.py`): Process-oriented framing with question archetypes (PREREQUISITES, PROCEDURE, APPROVAL, VERIFICATION) and search strategy (service/software/access name, onboarding procedures, approval workflows, how-to KB tags)

Both produce identical `ResolutionAnalysis` messages; `EvaluatorAgent` unchanged. Deleted: `src/agents_py/agents/decomposer/__init__.py`, `decomposer_agent` reference in `devui_serve.py`.

**Rationale:** (1) Better KB retrieval accuracy — type-specific search framing surfaces more relevant articles; (2) Cleaner intent preservation — incident/request distinction from classification carried through decomposition; (3) Prompt precision — incident engineers ask "what failed?" vs. service desk asks "what's needed?" — different prompts; (4) Debuggability — failure mode clearly incident-related or request-related, not ambiguous.

**Tradeoffs:** (+) More accurate question generation, better KB targeting; (-) Two agents to maintain, slightly more workflow complexity.

---

### 2026-05-04: Hicks — Gitignore baseline established

**By:** Hicks (Backend Dev)

**Status:** Implemented (2 commits on main, not pushed)

**Decision:** Added .NET standard .gitignore at repo root. Modified to preserve `.squad/log/` (project documentation) via negation pattern `!.squad/log/` after `[Ll]og/` exclusion.

**Rationale:** (1) Repo had no .gitignore; 184 build artifacts (155 bin/, 29 obj/) were tracked; (2) `.squad/log/` contains project session logs and architectural decisions (tracked), not build logs; (3) Negation pattern is specific and surgical.

**Files:** Created `/.gitignore` (487 lines via `dotnet new gitignore` + `!.squad/log/` negation). Untracked 184 build artifacts with `git rm --cached`. Commits: 9c98efa (add .gitignore), 7e121fd (untrack artifacts). No further cleanup needed.

---

### 2026-04-29T21:00:00Z: Apone & Bishop — Phase 2 Architecture & Search Index Schema

**By:** Apone (Lead) & Bishop (AI/Agents)

**What (Apone – Architecture):** Phase 2 establishes five integrated subsystems: (1) Azure Function (Consumption, .NET 10) as webhook receiver with HMAC validation; (2) Single AI Search index `tickets-index` with hybrid search (BM25 + vector, semantic reranking, text-embedding-3-small 1536d embeddings); (3) Two Foundry agents (gpt-4o-mini): triage-agent (classify, auto-resolve, escalate) and resolution-summarizer (polish resolutions); (4) PUT /api/tickets/{id} endpoint accepting structured agent results (state, assignedTo, resolutionNotes, agentAction, agentConfidence, matchedTicketNumber) with HMAC validation; (5) Bicep modules for AI Search (Basic), OpenAI (gpt-4o-mini + text-embedding-3-small), Foundry Hub/Project, Function App (Consumption), Storage Account. Incremental cost ~$78–81/mo. Nine gate criteria (G1–G9) lock sequencing: Hicks owns G1–G7 (infra/backend), Vasquez owns G8–G9 (tests), Bishop gated on G1–G7 for agent work.

**What (Bishop – Search Index):** Single index `tickets-index` with 14 fields: keyword fields (id, number, shortDescription, description, category, priority, state, assignedTo, caller, resolutionNotes) + vector field (contentVector, 1536d, cosine HNSW). Semantic config `ticket-semantic` uses shortDescription as title, [description, resolutionNotes] as content, [category, number] as keywords. Hybrid search strategy: embed concatenation of [shortDescription + description + category], query via BM25 + vector similarity, rerank semantically. Top 5 results passed to triage agent. Seed 25 pre-resolved IT scenarios (password resets, VPN, Outlook, etc.) into both SQL and index at gate time (G2/G7).

**Why:** Phase 2 kickoff locked. Architecture avoids premature complexity (no Durable Functions, no multi-index KB corpus, no visual Logic Apps), prioritizes demo reproducibility (`azd up`), enables parallel team tracks (Hicks on infra, Vasquez on tests, Bishop on standby), and establishes unambiguous dependencies via 9 gate criteria. Demo cost remains low (~$103/mo combined). All decisions made once; no blocking questions.

---

### 2026-04-29T17:50:00Z: Vasquez — Phase 2 SQL test infrastructure plan

**By:** Vasquez (Tester)


---

**Earlier decisions archived to `decisions-archive-2026-04-29.md`** (Phase 1 scope, resources, test infrastructure, etc.)
