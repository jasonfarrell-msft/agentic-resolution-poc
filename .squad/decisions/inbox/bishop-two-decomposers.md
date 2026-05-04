# Decision: Split DecomposerAgent into IncidentDecomposer + RequestDecomposer

**Date:** 2026-05-05  
**Author:** Bishop (AI/Agents Specialist)  
**Requested by:** Jason Farrell  

---

## Status

Implemented and validated.

---

## Context

The `DecomposerAgent` was a single agent that performed question-driven KB retrieval for all ticket types. It worked well for the core pattern (question generation → targeted search → synthesis) but used a generic prompt that did not distinguish between the reasoning process for incidents versus service requests.

Incident tickets require **failure-mode thinking**: something broke, and we need to understand root cause, scope, and recovery. Service request tickets require **fulfillment thinking**: something needs to be provisioned, and we need prerequisites, procedure, and approval clarity.

A single SYSTEM_PROMPT cannot simultaneously frame both mindsets at the level of precision needed for accurate KB retrieval.

---

## Decision

Split `DecomposerAgent` into two specialized agents with distinct SYSTEM_PROMPTs, question archetypes, and KB search strategies:

### IncidentDecomposer

**File:** `src/agents_py/agents/incident_decomposer/__init__.py`  
**Agent name:** `IncidentDecomposer`

- **Mindset:** Something has failed or degraded. Users are impacted now.
- **Question archetypes:**
  - ROOT CAUSE: What configuration/state produced this symptom?
  - SCOPE: What is the blast radius (one user vs. systemic)?
  - RECOVERY: What are the rollback or remediation steps?
  - VALIDATION: How do we confirm the fix worked and won't recur?
- **Search strategy:** Symptom patterns, component/service name, error codes, "troubleshooting/incident" KB tags
- **Tone:** Diagnostic, urgent, failure-analysis framing

### RequestDecomposer

**File:** `src/agents_py/agents/request_decomposer/__init__.py`  
**Agent name:** `RequestDecomposer`

- **Mindset:** Nothing is broken. A user wants something provisioned, granted, or set up.
- **Question archetypes:**
  - PREREQUISITES: What must be in place before fulfillment?
  - PROCEDURE: What are the exact provisioning/setup steps?
  - APPROVAL: Does this need manager or security sign-off?
  - VERIFICATION: How does the requester confirm fulfillment is complete?
- **Search strategy:** Service/software/access name, onboarding procedures, approval workflows, "how-to/request-fulfillment" KB tags
- **Tone:** Process-oriented, procedural, fulfillment framing

---

## Workflow Change

**Before:**
```
IncidentFetch → DecomposerExecutor → EvaluatorExecutor
RequestFetch  → DecomposerExecutor → EvaluatorExecutor
```

**After:**
```
IncidentFetch → IncidentDecomposerExecutor → EvaluatorExecutor
RequestFetch  → RequestDecomposerExecutor  → EvaluatorExecutor
```

Both decomposers produce identical `ResolutionAnalysis` messages. The `EvaluatorAgent` is unchanged. The `ResolutionQuestion` and `ResolutionAnalysis` message types are unchanged.

---

## Unchanged

- `shared/messages.py` — no changes to `ResolutionAnalysis`, `ResolutionQuestion`, `TicketDetails`
- `EvaluatorAgent` — unchanged; receives same structured input
- `ResolutionAgent`, `EscalationAgent` — unchanged
- Confidence threshold (0.80) — unchanged

---

## Deleted

- `src/agents_py/agents/decomposer/__init__.py` — superseded; deleted
- `decomposer_agent` reference in `devui_serve.py` — replaced with both specialized agents

---

## Rationale

1. **Better KB retrieval accuracy** — Type-specific search framing surfaces more relevant articles (troubleshooting guides vs. how-to guides are indexed differently)
2. **Cleaner intent preservation** — The incident/request distinction established at classification is now carried through decomposition, not collapsed to a single generic agent
3. **Prompt precision** — Incident engineers ask "what failed?" questions; service desk analysts ask "what's needed?" questions — these require different prompts
4. **Debuggability** — When a decomposer fails, the failure mode is clearly incident-related or request-related, not ambiguous

## Tradeoffs

- (+) More accurate question generation per ticket type
- (+) Better KB article targeting (symptom vs. procedure searches)
- (-) Two agents to maintain instead of one (prompts must be kept up to date independently)
- (-) Slightly more workflow complexity (two executor nodes instead of one)

---

## Escalation/Handoff

Both decomposers converge on `EvaluatorExecutor`. The Evaluator retains full authority over final confidence calibration and routes to `ResolutionAgent` (≥0.80) or `EscalationAgent` (<0.80) unchanged.
