# Resolution Pipeline Architecture — Visual Flow

## New Flow (Question-Driven KB Retrieval)

```
┌─────────────┐
│ TicketInput │ (INC0010091)
│  (DevUI)    │
└──────┬──────┘
       │
       v
┌──────────────────┐
│ ClassifierAgent  │ "Is this an Incident or Service Request?"
│  (MCP: get_ticket)│
└──────┬───────────┘
       │
       ├─── incident ───> ┌─────────────────┐
       │                  │ IncidentAgent   │ "Fetch full ticket details"
       │                  │ (MCP: get_ticket)│
       │                  └────────┬────────┘
       │                           │
       │                           v
       │                  ┌────────────────┐
       │                  │ TicketDetails  │ (ticket_description, category, priority)
       │                  └────────┬───────┘
       │                           │
       └─── request ────> ┌─────────────────┐
                          │ RequestAgent    │ "Fetch full ticket details"
                          │ (MCP: get_ticket)│
                          └────────┬────────┘
                                   │
                                   v
                          ┌────────────────┐
                          │ TicketDetails  │
                          └────────┬───────┘
                                   │
              ┌────────────────────┴────────────────────┐
              │                                          │
              v                                          │
      ┌───────────────────────────────────────┐         │
      │     DecomposerAgent                   │         │
      │  (NEW — Key Innovation)               │         │
      │                                        │         │
      │  STEP 1: Problem Understanding        │         │
      │    "What is broken/misconfigured?"    │         │
      │                                        │         │
      │  STEP 2: Question Generation          │         │
      │    Q1: "What VPN settings control     │         │
      │         split tunneling?"             │         │
      │    Q2: "Which cloud app domains       │         │
      │         should be excluded?"          │         │
      │                                        │         │
      │  STEP 3: Targeted KB Search           │         │
      │    🔍 search_knowledge_base(          │         │
      │         "VPN split tunneling config") │ ◄───────┤ Multiple MCP calls
      │    🔍 search_knowledge_base(          │         │ (iterative tool use)
      │         "cloud app direct access")    │         │
      │                                        │         │
      │  STEP 4: Answer Synthesis             │         │
      │    A1: "Split tunneling is in         │         │
      │         Network Settings > Advanced..." │       │
      │    A2: "Office 365 endpoints          │         │
      │         (*.office.com) should be..."  │         │
      │                                        │         │
      │  STEP 5: Preliminary Confidence       │         │
      │    0.88 (high)                        │         │
      └─────────────────┬─────────────────────┘
                        │
                        v
              ┌─────────────────────┐
              │ ResolutionAnalysis  │
              │                     │
              │ • core_problem      │
              │ • questions[2-4]    │
              │ • preliminary_conf  │
              └──────────┬──────────┘
                         │
                         v
              ┌──────────────────────────┐
              │   EvaluatorAgent          │
              │  (Receives structured     │
              │   analysis, not raw KB)   │
              │                           │
              │  STEP 1: Review Questions │
              │    "Do they address all   │
              │     aspects of problem?"  │
              │                           │
              │  STEP 2: Answer Complete  │
              │    "Are answers actionable│
              │     for a technician?"    │
              │                           │
              │  STEP 3: Coherence        │
              │    "Do answers work       │
              │     together logically?"  │
              │                           │
              │  STEP 4: Calibrated Conf  │
              │    0.87 (FINAL)           │
              └──────────┬───────────────┘
                         │
                         v
              ┌─────────────────────┐
              │ ResolutionProposal  │
              │                     │
              │ • resolution_text   │
              │ • confidence: 0.87  │
              │ • kb_source         │
              └──────────┬──────────┘
                         │
         ┌───────────────┴───────────────┐
         │                               │
    (≥0.80)                         (<0.80)
         │                               │
         v                               v
┌────────────────┐            ┌──────────────────┐
│ ResolutionAgent│            │ EscalationAgent  │
│ (MCP: update_  │            │ (MCP: update_    │
│  ticket with   │            │  ticket, assign  │
│  resolution)   │            │  to human group) │
└────────────────┘            └──────────────────┘
         │                               │
         v                               v
    ✅ Auto-Resolved               🔄 Escalated to Human
```

## Key Changes from Old Flow

### OLD (Dumb KB Search):
```
Incident/RequestAgent:
  1. get_ticket_by_number
  2. search_knowledge_base(short_description)  ← BLIND SEARCH
  3. Pick top 1 result
  4. Send raw KB article to Evaluator

EvaluatorAgent:
  - Receives: Raw KB article content + ticket description
  - Must reason about fit without understanding what info is needed
```

### NEW (Question-Driven):
```
Incident/RequestAgent:
  1. get_ticket_by_number
  2. Return ticket details (NO KB SEARCH)

DecomposerAgent:
  1. Understand problem: "What is broken?"
  2. Generate questions: "What specific info would resolve this?"
  3. Targeted searches: search_knowledge_base(question-specific terms) × N
  4. Synthesize answers from multiple KB articles
  5. Return structured analysis

EvaluatorAgent:
  - Receives: Structured analysis (questions + synthesized answers)
  - Evaluates if answers collectively resolve the ticket
```

## Message Flow Comparison

### OLD:
```
IncidentRoute → KBSearchResult → ResolutionProposal → Resolution/Escalation
                (raw KB article)
```

### NEW:
```
IncidentRoute → TicketDetails → ResolutionAnalysis → ResolutionProposal → Resolution/Escalation
                (ticket data)   (questions+answers)
```

## Why This Works Better

1. **Targeted Retrieval:** Instead of "VPN not connecting" (generic), we search for:
   - "VPN split tunneling configuration settings"
   - "cloud application direct access exclude VPN"
   
2. **Multi-Source Synthesis:** Can combine info from multiple KB articles:
   - "VPN Client Configuration Guide" (for split tunnel settings)
   - "Split Tunneling Best Practices" (for exclusion list)
   - "Cloud Access Optimization" (for domain patterns)

3. **Explicit Problem Decomposition:** Forces the LLM to articulate what needs to be known:
   - "What controls split tunneling?" (configuration question)
   - "Which domains to exclude?" (application question)

4. **Transparent Reasoning:** Questions + answers are logged, so when auto-resolve fails, we can see:
   - Were the right questions asked?
   - Did KB searches return relevant articles?
   - Were answers synthesized correctly?

## Performance Impact

| Metric              | OLD          | NEW             | Change      |
|---------------------|--------------|-----------------|-------------|
| KB searches/ticket  | 1            | 2-4             | +200-400%   |
| LLM calls/ticket    | 3            | 4               | +33%        |
| Token usage/ticket  | ~6K          | ~12K            | +100%       |
| Latency (seconds)   | ~3-5         | ~8-15           | +5-10 sec   |
| Cost per ticket     | ~$0.0002     | ~$0.0005        | +$0.0003    |
| **Expected accuracy** | ~40% auto-resolve | ~60%+ auto-resolve | **+50% improvement** |

## Next Steps for Optimization

1. **Cache KB search results** — If 2 tickets ask "VPN split tunneling", reuse the search results
2. **Question quality gate** — Meta-prompt to validate questions before searching
3. **Feedback loop** — Log {questions, confidence, actual_outcome} → fine-tune prompts
4. **Dynamic question count** — Let agent decide 1-5 questions based on complexity
