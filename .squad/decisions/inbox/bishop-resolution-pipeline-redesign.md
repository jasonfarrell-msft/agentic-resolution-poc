# Resolution Pipeline Redesign: Question-Driven KB Retrieval

**Author:** Bishop (AI/Agents Specialist)  
**Date:** 2026-05-04  
**Status:** Implemented  
**Context:** Multi-agent resolution pipeline redesign to improve auto-resolve accuracy

## Problem Statement

The current resolution pipeline has a fundamental flaw: it performs "dumb" KB search using only the ticket's short description, picks the top result, and then asks an evaluator to reason about fit. This approach fails because:

1. **Search is untargeted** — we search with a general query, not specific questions
2. **No problem decomposition** — we don't identify what information is actually needed to solve the problem
3. **Single KB article assumption** — we only retrieve one article, but the answer might require synthesizing multiple sources
4. **Evaluator reasoning happens too late** — by the time we evaluate, we've already picked the wrong KB article

**Test case:** INC0010091 "VPN split tunneling misconfigured causing slow cloud app access" — system fails to confidently match it to the "VPN Not Connecting" KB article because the search term doesn't capture the specific problem (split tunneling vs connection).

## Solution: Question-Driven Resolution Pipeline

### Architecture Decision

**Use a single enriched agent with multiple search iterations**, not separate agents per step. Rationale:

- **Agent Framework strength:** Agents can natively call MCP tools in a loop — tool use is built-in
- **Reduced serialization overhead:** Each agent boundary requires message serialization/deserialization
- **Natural flow:** An LLM can naturally perform "think → search → search → synthesize → evaluate" in one conversation
- **Simpler debugging:** All reasoning steps visible in one agent trace
- **Fewer workflow edges:** Simpler workflow graph = easier to maintain

The key innovation: **ask the agent to decompose the problem into specific answerable questions BEFORE searching**, then search multiple times to answer each question.

### New Flow

```
Classifier → Incident/Request Agent → [NEW] DecomposerAgent → EvaluatorAgent → Gate → Resolution/Escalation
```

**Step-by-step:**

1. **DecomposerAgent** receives ticket details (from Incident/Request agent)
   - Understands the core problem
   - Generates 2-4 specific questions that must be answered to resolve it
   - For each question, proposes search terms to find answers
   - Executes targeted KB searches (multiple calls to `search_knowledge_base`)
   - Synthesizes the KB search results into specific answers
   - Outputs a structured `ResolutionAnalysis` with questions + answers + KB sources

2. **EvaluatorAgent** receives the analysis (NOT raw KB articles)
   - Reviews each question and its synthesized answer
   - Determines if the answers collectively resolve the ticket
   - Assigns calibrated confidence (0.0-1.0)
   - Produces final `ResolutionProposal`

3. **Confidence gate** (unchanged)
   - ≥0.80 → ResolutionAgent
   - <0.80 → EscalationAgent

### New Message Types

```python
@dataclass
class ResolutionQuestion:
    """A specific question that must be answered to resolve the ticket."""
    question: str          # "What causes VPN split tunneling to route cloud traffic incorrectly?"
    search_terms: str      # "VPN split tunneling configuration cloud routing"
    answer: str            # Synthesized answer from KB search results
    kb_sources: list[str]  # ["VPN Not Connecting", "Cloud Access Best Practices"]

@dataclass
class ResolutionAnalysis:
    """Output from DecomposerAgent: problem breakdown + targeted KB retrieval + synthesis."""
    ticket_number: str
    ticket_id: str
    short_description: str
    ticket_description: str
    ticket_category: str
    ticket_priority: str
    ticket_type: str  # "incident" or "request"
    
    # Decomposition
    core_problem: str          # 1-sentence statement of the root issue
    questions: list[ResolutionQuestion]  # 2-4 specific questions with answers
    
    # Confidence (preliminary)
    preliminary_confidence: float  # Agent's initial assessment before formal evaluation
```

The existing `KBSearchResult` message is **removed** — we no longer pass raw KB articles downstream.

### Agent Prompts

#### DecomposerAgent

```
You are an IT problem decomposition and knowledge synthesis agent. Your job is to:
1. Understand what problem the ticket describes
2. Identify specific questions that must be answered to resolve it
3. Search the knowledge base multiple times with targeted queries
4. Synthesize the search results into clear answers

You have access to the search_knowledge_base tool. Use it strategically — not just once with the ticket description, but multiple times with specific, focused queries.

WORKFLOW:

STEP 1 — PROBLEM UNDERSTANDING
Read the ticket description carefully. In 1-2 sentences, state:
- What is broken, missing, or misconfigured?
- What would a complete resolution look like?

STEP 2 — QUESTION GENERATION
Generate 2-4 specific, answerable questions that would lead to a resolution. Each question should:
- Target a specific aspect of the problem
- Be answerable from documentation or knowledge base content
- Not be a restatement of the ticket — dig deeper

Examples:
- Bad: "How do I fix VPN issues?" (too broad)
- Good: "What VPN settings control which traffic routes through the tunnel vs. direct internet?"
- Bad: "Why isn't this working?" (vague)
- Good: "What authentication method does this application require for SSO?"

STEP 3 — TARGETED KB SEARCH
For each question:
- Formulate a specific search query (use technical terms, product names, error codes)
- Call search_knowledge_base with that query
- Review the top 2-3 results
- If needed, search again with a refined query

Do NOT just search once with the ticket's short description. That's the old approach that fails.

STEP 4 — ANSWER SYNTHESIS
For each question, write a clear, specific answer based on the KB articles you found:
- If KB articles provide a direct answer, synthesize it (don't copy-paste verbatim)
- If multiple articles contribute, combine them
- If no KB article addresses the question, state: "No KB documentation found for this specific issue."
- Reference which KB article(s) informed your answer

STEP 5 — PRELIMINARY CONFIDENCE
Based on the completeness and specificity of your answers, assign a preliminary confidence:
- 0.85+ : All questions have clear, actionable answers directly from KB articles
- 0.70-0.84 : Most questions answered, but some gaps or need interpretation
- 0.50-0.69 : Partial answers only, significant gaps remain
- Below 0.50 : No relevant KB documentation found

OUTPUT FORMAT (JSON):
```json
{
  "core_problem": "VPN split tunneling is routing cloud application traffic through the corporate network instead of direct internet, causing latency.",
  "questions": [
    {
      "question": "What VPN settings control split tunneling behavior?",
      "search_terms": "VPN split tunneling configuration settings",
      "answer": "Split tunneling is configured in the VPN client profile under 'Network Settings > Advanced > Split Tunnel Mode'. Setting it to 'Include' routes only specified subnets through VPN; 'Exclude' routes all traffic except specified subnets directly.",
      "kb_sources": ["VPN Client Configuration Guide", "Split Tunneling Best Practices"]
    },
    {
      "question": "Which cloud application domains should be excluded from the VPN tunnel?",
      "search_terms": "cloud application direct access exclude VPN",
      "answer": "Office 365 endpoints (*.office.com, *.microsoft.com), Salesforce (*.salesforce.com), and internal SaaS apps should be in the split tunnel exclusion list to optimize performance.",
      "kb_sources": ["Cloud Access Optimization", "VPN Split Tunneling Best Practices"]
    }
  ],
  "preliminary_confidence": 0.88,
  "ticket_id": "the GUID provided to you"
}
```

CRITICAL REMINDERS:
- Call search_knowledge_base MULTIPLE TIMES (once per question minimum)
- Do NOT settle for the first search result — refine and search again if needed
- Synthesize answers in your own words — don't dump raw KB content
- Be specific: reference KB article titles in kb_sources
```

#### EvaluatorAgent (Updated)

The evaluator now receives a `ResolutionAnalysis` instead of raw KB articles, so the prompt is simplified:

```
You are an IT Resolution Evaluator. You receive a structured analysis of a ticket problem that includes:
- The core problem statement
- 2-4 specific questions that were asked
- Synthesized answers from knowledge base searches
- Preliminary confidence assessment

Your job: determine if the provided answers COLLECTIVELY resolve the ticket's problem.

WORKFLOW:

STEP 1 — REVIEW PROBLEM + QUESTIONS
Do the questions address all aspects of the ticket's problem? Are there gaps?

STEP 2 — ANSWER COMPLETENESS
For each question:
- Is the answer specific and actionable?
- Does it reference actual KB documentation?
- Would a technician know what to do based on this answer?

STEP 3 — SOLUTION COHERENCE
If a technician followed all the answers as instructions:
- Would the ticket's problem be fully resolved?
- Are the steps logically ordered?
- Do the answers complement each other, or are there conflicts?

STEP 4 — CALIBRATED CONFIDENCE
Assign your final confidence score:
- 0.90+ : Answers are complete, actionable, directly address the problem — high confidence resolution
- 0.75-0.89 : Answers cover the problem but may need minor technician judgment or adaptation
- 0.50-0.74 : Partial solution only — significant gaps or ambiguity remain
- Below 0.50 : Answers do not adequately resolve this specific ticket

IMPORTANT:
- Be STRICT. Auto-resolution means no human review — only approve if you're confident a technician could execute blindly.
- If preliminary_confidence was high but you spot issues, override it downward.
- Your confidence score determines whether the ticket auto-resolves (≥0.80) or escalates to a human.

OUTPUT FORMAT (JSON):
```json
{
  "confidence": 0.87,
  "resolution_text": "Reconfigure the VPN client split tunneling: Go to VPN Settings > Advanced > Split Tunnel Mode, set to 'Exclude', add cloud app domains (*.office.com, *.salesforce.com) to exclusion list. Test cloud app access after reconnecting VPN.",
  "kb_source": "VPN Client Configuration Guide; Split Tunneling Best Practices; Cloud Access Optimization",
  "ticket_id": "the GUID provided to you"
}
```

The resolution_text should be a CONSOLIDATED step-by-step instruction for a technician, synthesized from all the answers. Reference the ticket's specifics.
```

## Implementation Changes

### Files Modified

1. **`src/agents_py/shared/messages.py`** — Add `ResolutionQuestion` and `ResolutionAnalysis` dataclasses; remove `KBSearchResult` (breaking change)

2. **`src/agents_py/agents/decomposer/__init__.py`** (NEW) — DecomposerAgent with full prompt + MCP tool

3. **`src/agents_py/agents/evaluator/__init__.py`** — Update prompt to expect `ResolutionAnalysis` instead of raw KB data

4. **`src/agents_py/agents/incident/__init__.py`** — Simplified: only fetch ticket details, emit `TicketDetails` message (no KB search)

5. **`src/agents_py/agents/request/__init__.py`** — Simplified: only fetch ticket details, emit `TicketDetails` message (no KB search)

6. **`src/agents_py/workflow/__init__.py`** — Full rewrite:
   - Add `decompose_problem` executor (calls DecomposerAgent)
   - Update `evaluate_resolution` to accept `ResolutionAnalysis`
   - Update workflow edges: Classifier → Incident/Request → **Decomposer** → Evaluator → Gate → Resolution/Escalation

7. **`src/agents_py/devui_serve.py`** — Add `decomposer_agent` to served entities

### Backward Compatibility

**Breaking change:** The `KBSearchResult` message is removed. Any external consumers (e.g., .NET API calling this workflow) will need to be updated if they expected that message type. However, since this is a Python-only workflow called via the devui, and the .NET side uses its own agent implementation, this should not break production.

## Tradeoffs

### Advantages
- **Higher accuracy:** Targeted KB searches based on specific questions vs. blind top-1 retrieval
- **Better reasoning:** LLM explicitly articulates what needs to be known before searching
- **Multi-source synthesis:** Can combine information from multiple KB articles
- **Transparency:** Questions + answers are logged, making failures easier to diagnose

### Disadvantages
- **Increased latency:** Multiple KB searches (2-4+) vs. single search = longer resolution time (~5-10 sec increase)
- **Higher cost:** More LLM calls (decomposer does reasoning + multiple searches; evaluator still needed)
- **Complexity:** More sophisticated prompts = harder to debug hallucination/off-track reasoning

### Cost Estimate (per ticket)
- Old flow: ~3 LLM calls (classifier, retriever, evaluator) = ~6K tokens
- New flow: ~4 LLM calls (classifier, retriever, decomposer, evaluator) = ~12K tokens (decomposer does multiple searches in one session)
- At gpt-5.1-deployment pricing (~$10/1M input, $30/1M output): +$0.0003 per ticket
- Acceptable for demo; production would optimize with caching

## Success Metrics

After deployment, measure:
1. **Auto-resolve rate** — % of tickets resolved with confidence ≥0.80 (target: increase from current ~40% to 60%+)
2. **False positive rate** — % of auto-resolved tickets that users re-open (target: <5%)
3. **Average questions generated** — DecomposerAgent should generate 2-4 questions per ticket (monitor for degenerate 1-question cases)
4. **KB search calls per ticket** — Should be 2-4+ (proves we're doing targeted retrieval)

## Next Steps (Future Work)

1. **Caching layer:** Cache KB search results for common queries to reduce MCP server load
2. **Question quality validation:** Add a meta-prompt to validate generated questions before searching
3. **Feedback loop:** Log { ticket, questions, answers, confidence, actual_resolution_outcome } → fine-tune question generation
4. **Dynamic question count:** Let the agent decide how many questions (1-5) based on problem complexity

---

**Decision:** Approved and implemented by Bishop, 2026-05-04.
