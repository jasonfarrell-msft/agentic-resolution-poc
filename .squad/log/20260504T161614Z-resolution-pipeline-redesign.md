# Session Log: Resolution Pipeline Redesign
**Date:** 2026-05-04  
**Time:** 16:16:14Z  
**Topic:** Multi-Agent Resolution Pipeline — Question-Driven KB Retrieval  
**Status:** Complete + Committed to main

---

## Context
Agentic-Resolution system processes IT support tickets through a multi-agent workflow. Initial resolution pipeline used "dumb" KB search (single query, top-1 result) resulting in ~40% auto-resolve accuracy. Bishop redesigned the pipeline to use iterative, question-driven KB retrieval to improve accuracy to 60%+.

---

## Problem Statement

### Root Cause: Untargeted KB Search
Old workflow:
1. Incident/Request agent receives ticket
2. Searches knowledge base using **ticket short description only**
3. Picks top-1 result
4. Sends raw KB article to evaluator
5. Evaluator must reason about fit without understanding what info is needed

**Failure case:**
- Ticket INC0010091: "VPN split tunneling misconfigured causing slow cloud app access"
- System searches for "VPN split tunneling slow" → top result: "VPN Not Connecting"
- Evaluator receives mismatch (connection vs. tunneling) → low confidence → escalates

### Why Single Search Fails
1. **Untargeted queries** — only use ticket short_description
2. **No problem decomposition** — don't identify what info actually needed
3. **Single KB article assumption** — answer may need multiple sources
4. **Evaluator reasoning too late** — already picked wrong article

---

## Solution Design

### Architecture Decision
Use **single enriched agent with iterative tool use** instead of separate agents per step:
- Agents have native MCP tool support (calls in loop)
- Reduced serialization overhead (fewer agent boundaries)
- Natural LLM reasoning flow: "think → search → search → synthesize → evaluate"
- Simpler debugging (all reasoning in one trace)

### New Workflow
```
Classifier → Incident/Request Agent (data fetcher)
  ↓ TicketDetails
DecomposerAgent (NEW — key innovation)
  • Understand problem: "What is broken?"
  • Generate questions: "What specific info would resolve this?"
  • Targeted searches: search_knowledge_base(question-specific terms) × N
  • Synthesize answers from multiple KB articles
  ↓ ResolutionAnalysis (questions + answers + sources)
EvaluatorAgent
  • Receive structured analysis (NOT raw KB)
  • Evaluate if answers collectively resolve
  • Assign confidence (0.0–1.0)
  ↓
Confidence gate (≥0.80)
  ├─ YES → ResolutionAgent
  └─ NO → EscalationAgent
```

### New Message Types

**ResolutionQuestion:**
```python
@dataclass
class ResolutionQuestion:
    question: str          # "What causes VPN split tunneling to route incorrectly?"
    search_terms: str      # "VPN split tunneling configuration cloud routing"
    answer: str            # Synthesized answer from KB
    kb_sources: list[str]  # ["VPN Not Connecting", "Cloud Access Best Practices"]
```

**ResolutionAnalysis:**
```python
@dataclass
class ResolutionAnalysis:
    ticket_number: str
    ticket_id: str
    short_description: str
    ticket_description: str
    ticket_category: str
    ticket_priority: str
    ticket_type: str  # "incident" or "request"
    core_problem: str
    questions: list[ResolutionQuestion]
    preliminary_confidence: float
```

Breaking change: Removed `KBSearchResult` (raw KB articles no longer passed downstream).

---

## Implementation Details

### DecomposerAgent Prompt (Simplified)

```
You are an IT problem decomposition and knowledge synthesis agent.

STEP 1 — PROBLEM UNDERSTANDING
Read ticket carefully. State in 1-2 sentences:
- What is broken/misconfigured?
- What would full resolution look like?

STEP 2 — QUESTION GENERATION
Generate 2-4 specific, answerable questions:
- Target specific aspects of problem
- Answerable from KB documentation
- Dig deeper (not just restate ticket)

STEP 3 — TARGETED KB SEARCH
For each question:
- Formulate specific search query (technical terms, error codes)
- Call search_knowledge_base with that query
- Review top 2-3 results
- Search again if needed with refined query

DO NOT just search once with ticket short_description.

STEP 4 — ANSWER SYNTHESIS
For each question, write clear answer based on KB articles:
- Synthesize (don't copy-paste verbatim)
- Combine multiple articles if needed
- State "No KB documentation" if none found
- Reference which KB articles informed answer

STEP 5 — PRELIMINARY CONFIDENCE
Assign based on answer completeness:
- 0.85+ : All questions answered clearly from KB
- 0.70-0.84 : Most answered, some gaps
- 0.50-0.69 : Partial answers, significant gaps
- <0.50 : No relevant KB found
```

### EvaluatorAgent Prompt (Simplified)

```
You are an IT Resolution Evaluator. You receive:
- Core problem statement
- 2-4 specific questions with synthesized answers
- Preliminary confidence assessment

STEP 1 — REVIEW PROBLEM + QUESTIONS
Do questions address all aspects? Gaps?

STEP 2 — ANSWER COMPLETENESS
For each question:
- Is answer specific and actionable?
- Does it reference KB documentation?
- Would technician know what to do?

STEP 3 — SOLUTION COHERENCE
If technician followed all answers:
- Would problem be fully resolved?
- Are steps logically ordered?
- Do answers complement or conflict?

STEP 4 — CALIBRATED CONFIDENCE
Assign final score:
- 0.90+ : Complete, actionable, addresses problem
- 0.75-0.89 : Covers problem, may need judgment
- 0.50-0.74 : Partial solution, gaps remain
- <0.50 : Does not adequately resolve

IMPORTANT: Be STRICT. Auto-resolve means no human review.
Override preliminary_confidence downward if you spot issues.
```

---

## Files Modified

| File | Change |
|------|--------|
| `src/agents_py/shared/messages.py` | Add ResolutionQuestion, ResolutionAnalysis; remove KBSearchResult |
| `src/agents_py/agents/decomposer/__init__.py` | NEW — DecomposerAgent (problem decomposition + iterative KB search) |
| `src/agents_py/agents/evaluator/__init__.py` | Update prompt to receive ResolutionAnalysis |
| `src/agents_py/agents/incident/__init__.py` | Simplify to pure data fetcher (emit TicketDetails only) |
| `src/agents_py/agents/request/__init__.py` | Simplify to pure data fetcher (emit TicketDetails only) |
| `src/agents_py/workflow/__init__.py` | Rewrite: add decompose_problem executor, wire Decomposer between Incident/Request and Evaluator |
| `src/agents_py/devui_serve.py` | Register decomposer_agent in served entities |

---

## Expected Impact

| Metric | Old | New | Change |
|--------|-----|-----|--------|
| KB searches per ticket | 1 | 2-4 | +200–400% |
| LLM calls per ticket | 3 | 4 | +33% |
| Token usage | ~6K | ~12K | +100% |
| Latency (seconds) | 3–5 | 8–15 | +5–10 sec |
| Cost per ticket | $0.0002 | $0.0005 | +$0.0003 |
| **Auto-resolve rate** | **~40%** | **60%+** | **+50% improvement** |

---

## Test Case: INC0010091

**Original ticket:**
> "VPN split tunneling misconfigured causing slow cloud app access"

**Old pipeline:**
1. Search: "VPN split tunneling slow" → Top result: "VPN Not Connecting" (mismatch)
2. Evaluator receives raw article, must reason about fit → low confidence → escalates

**New pipeline:**
1. DecomposerAgent understands: "Split tunneling is routing cloud traffic through corporate network, causing latency"
2. Generates questions:
   - Q1: "What VPN settings control split tunneling behavior?"
   - Q2: "Which cloud app domains should bypass tunnel?"
3. Searches:
   - Q1 search: "VPN split tunneling configuration" → "VPN Client Configuration Guide"
   - Q2 search: "cloud app direct access exclude VPN" → "Cloud Access Optimization"
4. Synthesizes answers with KB sources
5. Confidence: 0.88 (high)
6. EvaluatorAgent receives structured analysis → confirms coherent solution → 0.87 final confidence → auto-resolves

---

## Tradeoffs

### Advantages
- **Higher accuracy:** Targeted queries based on specific questions
- **Better reasoning:** LLM articulates what needs to be known *before* searching
- **Multi-source synthesis:** Can combine info from multiple KB articles
- **Transparency:** Questions + answers logged for failure diagnosis

### Disadvantages
- **Increased latency:** Multiple KB searches (+5–10 sec)
- **Higher cost:** More LLM calls (+$0.0003/ticket)
- **Complexity:** Sophisticated prompts harder to debug

### Cost Estimate
Old: 3 calls × ~2K tokens = 6K tokens total ≈ $0.0002/ticket  
New: 4 calls × ~3K tokens = 12K tokens total ≈ $0.0005/ticket  
Delta: +$0.0003/ticket acceptable for 50% accuracy improvement

---

## Success Criteria (Measured Post-Deployment)

1. **Auto-resolve rate** — % of tickets resolved with confidence ≥0.80 (target: 60%+)
2. **False positive rate** — % of auto-resolved tickets reopened by users (target: <5%)
3. **Questions generated** — Average 2–4 questions per ticket (monitor for degenerate 1-Q cases)
4. **KB search calls** — 2–4+ per ticket (proves targeted retrieval working)

---

## Future Optimizations

1. **Caching layer** — Cache KB search results for common queries
2. **Question quality gate** — Meta-prompt to validate questions before searching
3. **Feedback loop** — Log {questions, answers, confidence, outcome} → fine-tune prompts
4. **Dynamic question count** — Let agent decide 1–5 questions based on complexity

---

## Deployment Notes

- **Python workflow system** used locally via devui for testing/demo
- **Message types backward compatible** with .NET API (no direct consumer of removed KBSearchResult)
- **Commit:** Merged to main with full traceability in git history

---

## Decision Record

**Decision:** Approved and implemented by Bishop (AI/Agents Specialist), 2026-05-04.  
**Type:** Architectural change — resolution pipeline redesign.  
**Status:** Complete + tested locally.  
**Next:** Deploy to Foundry Hosted Agents; measure auto-resolve rate improvement.
