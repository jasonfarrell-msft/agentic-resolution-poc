# Skill: Foundry Multi-Agent Pipeline with Classification Routing

**Domain:** Azure AI Foundry Agents / .NET orchestration  
**Reusable in:** Any project using `Azure.AI.Agents.Persistent` that chains multiple specialist agents.

---

## Pattern: Classify-then-Route

When a single resolution agent handles heterogeneous inputs poorly, add a lightweight classification stage first:

```
Webhook → ClassificationAgent → [IncidentAgent | RequestAgent] → WriteBack
                                          ↓ fallback
                                    ResolutionAgent (safety net)
```

### Implementation skeleton

```csharp
public async Task<AgentRunResult> RunAgentPipelineAsync(Snapshot snapshot, CancellationToken ct)
{
    var classification = await RunClassificationAgentAsync(snapshot, ct);

    var result = classification.Classification switch
    {
        "incident" => await RunIncidentAgentAsync(snapshot, ct),
        "request"  => await RunRequestAgentAsync(snapshot, ct),
        _          => await RunFallbackAgentAsync(snapshot, ct)  // safety net
    };

    var final = result with { Classification = classification.Classification };
    if (final.Success) await WriteBackAsync(snapshot.Id, final, ct);
    return final;
}
```

---

## Pattern: Dual JSON Schema Parsing

When an agent emits a different JSON schema from other agents, use a dedicated raw-text extraction path to avoid losing the response:

**Problem:** A shared `RunAndPollAsync` that calls `ParseAgentResponse` hardcodes the expected JSON keys (`action`, `confidence`, `notes`, `matchedTicketNumber`). A classification agent emits `{ "classification", "confidence", "rationale" }` — different keys. If both go through `ParseAgentResponse`, the classification values are silently dropped.

**Solution:** Two helpers:

```csharp
// Standard: polls + parses with standard JSON schema
private Task<AgentRunResult> RunAndPollAsync(...) { ... return ParseAgentResponse(rawText); }

// Custom: polls + returns raw text (caller parses with its own schema)
private Task<string> RunAndPollRawTextAsync(...) { ... return rawText; }
```

Classification agent uses `RunAndPollRawTextAsync` + `ParseClassificationResponse`:

```csharp
var rawText = await RunAndPollRawTextAsync(agentsClient, agent, userMessage, ct);
// rawText contains the full assistant response; parse classification-specific keys
var start = rawText.LastIndexOf("```json", StringComparison.OrdinalIgnoreCase);
// ... extract "classification" | "confidence" | "rationale"
```

This avoids modifying `ParseAgentResponse` or introducing a flag parameter — clean separation.

---

## Pattern: Classification Normalisation

Always normalise classification output to a known set before routing:

```csharp
if (classification is not ("incident" or "request"))
    classification = null;  // triggers fallback route
```

Never trust raw LLM output for routing decisions without normalisation.

---

## Pattern: State Transition Switch Expression

Use a switch expression in `WriteBackAsync` for clean multi-agent state routing:

```csharp
ticket.State = result.Action switch
{
    "incident_auto_resolved" when result.Confidence >= 0.8f => TicketState.Resolved,
    "request_auto_queued"    => TicketState.InProgress,
    "request_needs_approval" => TicketState.OnHold,
    _ when result.Action != "escalate" && result.Confidence >= 0.8f => TicketState.Resolved,
    _ => ticket.State   // leave unchanged
};
```

---

## Notes

- All agent prompts remain in `AgentDefinitions.cs` as versioned constants — never inline.
- Agent IDs are cached in `static ConcurrentDictionary<string, string>` — works across all 5+ agents.
- `TicketWebhookSnapshot` stays lean (no Description) — agents fetch full record via `get_ticket_by_number`.
- Classification defaults to "incident" on ambiguity (fail-safe over-triage).
- `RunResolutionAgentAsync` is preserved as fallback — never remove the safety net.
