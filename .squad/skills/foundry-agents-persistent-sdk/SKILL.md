# Skill: Azure AI Agents Persistent SDK Patterns

**Domain:** Azure AI Foundry — Agent Orchestration  
**Applicable to:** Any .NET project wiring Foundry agents with MCP tool sources  
**Last verified:** Azure.AI.Projects 1.0.0-beta.9 + Azure.AI.Agents.Persistent 1.2.0-beta.8

---

## Package Setup

```xml
<PackageReference Include="Azure.AI.Projects" Version="1.0.0-beta.9" />
<PackageReference Include="Azure.AI.Agents.Persistent" Version="1.2.0-beta.8" />
```

`Azure.AI.Projects` does NOT auto-install `Azure.AI.Agents.Persistent` — add it explicitly.

---

## AIProjectClient Endpoint Format

Beta.9 requires the new endpoint format:
```
https://{aiservices-account-name}.services.ai.azure.com/api/projects/{project-name}
```

The old cognitiveservices.azure.com format does NOT work with beta.9.

---

## Creating the Client

```csharp
var projectClient = new AIProjectClient(new Uri(endpoint), credential);
// PersistentAgentsClient has no public ctor — must use this extension:
var agentsClient = projectClient.GetPersistentAgentsClient(); // from Azure.AI.Agents.Persistent
```

---

## MCP Tool Integration

```csharp
// Native MCP tool — Foundry connects to the SSE endpoint automatically
var mcpTool = new MCPToolDefinition("my-server-label", "https://my-mcp-server.example.com/mcp");
```

`MCPToolDefinition` is only available in `Azure.AI.Agents.Persistent` >= 1.2.0-beta.5.

---

## Creating an Agent

```csharp
var agent = (await agentsClient.Administration.CreateAgentAsync(
    model: "gpt-41-mini",
    name: "my-agent",
    description: null,
    instructions: "Your system prompt here",
    tools: [mcpTool],
    toolResources: null,
    temperature: null, topP: null, responseFormat: null, metadata: null,
    cancellationToken: ct)).Value;
```

---

## Thread → Message → Run → Poll

```csharp
var thread = (await agentsClient.Threads.CreateThreadAsync(
    messages: null, toolResources: null, metadata: null, cancellationToken: ct)).Value;

await agentsClient.Messages.CreateMessageAsync(
    thread.Id, MessageRole.User, userMessage,
    attachments: null, metadata: null, cancellationToken: ct);

var run = (await agentsClient.Runs.CreateRunAsync(thread, agent, ct)).Value;

// Poll until terminal
while (!IsTerminal(run.Status))
{
    await Task.Delay(1_000, ct);
    run = (await agentsClient.Runs.GetRunAsync(thread.Id, run.Id, ct)).Value;
}

static bool IsTerminal(RunStatus s) =>
    s.Equals(RunStatus.Completed) || s.Equals(RunStatus.Failed) ||
    s.Equals(RunStatus.Cancelled) || s.Equals(RunStatus.Expired) ||
    s.Equals(RunStatus.Incomplete);
```

**Important:** `RunStatus` is a **struct**, not an enum. Use `.Equals()`, not `==`.

---

## Reading the Last Assistant Message

```csharp
await foreach (var msg in agentsClient.Messages.GetMessagesAsync(thread.Id, run.Id, cancellationToken: ct))
{
    if (msg.Role == MessageRole.Agent)
    {
        var text = msg.ContentItems.OfType<MessageTextContent>().LastOrDefault()?.Text;
        break;
    }
}
```

---

## Get-or-Create Agent Pattern

```csharp
private static readonly ConcurrentDictionary<string, string> _agentIdCache = new();

private async Task<PersistentAgent> GetOrCreateAsync(PersistentAgentsClient client, string name, ...)
{
    if (_agentIdCache.TryGetValue(name, out var id))
    {
        try { return (await client.Administration.GetAgentAsync(id, ct)).Value; }
        catch { _agentIdCache.TryRemove(name, out _); }
    }
    await foreach (var a in client.Administration.GetAgentsAsync(cancellationToken: ct))
    {
        if (a.Name == name) { _agentIdCache[name] = a.Id; return a; }
    }
    var created = (await client.Administration.CreateAgentAsync(...)).Value;
    _agentIdCache[name] = created.Id;
    return created;
}
```

---

## Fire-and-Forget from BackgroundService (Scoped dependency)

```csharp
// In BackgroundService ctor, inject IServiceScopeFactory
_ = Task.Run(async () =>
{
    try
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<MyAgentService>();
        await svc.RunAsync(snapshot, CancellationToken.None);
    }
    catch (Exception ex) { _logger.LogError(ex, "Agent run failed"); }
});
```
