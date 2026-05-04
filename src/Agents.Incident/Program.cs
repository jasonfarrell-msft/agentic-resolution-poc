using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using OpenAI.Chat;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks();
builder.Services.AddApplicationInsightsTelemetry();

var app = builder.Build();
app.MapHealthChecks("/health");

app.MapPost("/process", async (ProcessRequest req, IHttpClientFactory httpFactory, IConfiguration config,
    ILogger<Program> logger, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.TicketNumber))
        return Results.BadRequest("ticketNumber is required");

    string mcpUrl = config["MCP_SERVER_URL"]
        ?? "https://ca-mcp-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io/mcp";
    string aiEndpoint = config["AZURE_AI_ENDPOINT"]
        ?? throw new InvalidOperationException("AZURE_AI_ENDPOINT not configured");
    string? clientId = config["AZURE_CLIENT_ID"];

    TokenCredential credential = string.IsNullOrWhiteSpace(clientId)
        ? new DefaultAzureCredential()
        : new ManagedIdentityCredential(clientId);

    var http = httpFactory.CreateClient();
    try
    {
        string ticketJson = await CallMcpToolAsync(http, mcpUrl, "get_ticket_by_number",
            new { ticket_number = req.TicketNumber }, logger, ct);

        JsonNode ticketNode = JsonNode.Parse(ticketJson)!;
        string ticketId = ticketNode["id"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"Ticket {req.TicketNumber} has no 'id' field");
        string ticketState = ticketNode["state"]?.GetValue<string>() ?? string.Empty;
        if (string.Equals(ticketState, "Closed", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Ticket {TicketNumber} is already Closed — skipping processing", req.TicketNumber);
            return Results.Ok(new ResolutionResult("skipped", 0.0, "Ticket is already closed.", null));
        }
        string shortDescription = ticketNode["shortDescription"]?.GetValue<string>() ?? req.TicketNumber;

        string searchJson = await CallMcpToolAsync(http, mcpUrl, "search_tickets",
            new { query = shortDescription, state = "Resolved", page_size = 5 }, logger, ct);

        string kbJson = await CallMcpToolAsync(http, mcpUrl, "search_knowledge_base",
            new { query = shortDescription, top_k = 5 }, logger, ct);

        // Build Azure OpenAI client for the configured deployment
        string modelDeployment = "gpt-41-mini";
        var azureAiClient = new AzureOpenAIClient(new Uri(aiEndpoint), credential);
        var chatClient = azureAiClient.GetChatClient(modelDeployment);
        string userContent =
            $"Ticket data:\n{ticketJson}\n\nSimilar resolved incidents (for reference):\n{searchJson}" +
            $"\n\nKnowledge base articles (scored 0.0-1.0; use score >= 0.8 for auto-resolution):\n{kbJson}";

        string systemPrompt =
            "You are an IT incident resolution agent. You handle tickets classified as Incidents\n" +
            "(something broken, degraded, or causing harm).\n\n" +
            "You are given the full ticket data, a list of similar resolved incidents, and\n" +
            "knowledge base articles retrieved specifically for this issue.\n\n" +
            "Resolution logic:\n" +
            "1. Check the KB articles first. If a KB article has a score >= 0.8 and its resolution\n" +
            "   steps clearly apply to this ticket, use it to auto-resolve.\n" +
            "2. If no KB article meets the threshold, check the similar resolved incidents for a match.\n" +
            "3. If neither source provides a high-confidence resolution, escalate.\n\n" +
            "When auto-resolving (action: \"incident_auto_resolved\"):\n" +
            "- Set confidence to the KB article score (or incident match score), rounded to 2 decimal places.\n" +
            "- Set notes to a concise resolution summary citing the KB article title or matched ticket.\n\n" +
            "When escalating (action: \"escalate_incident\"):\n" +
            "- Set confidence to the best score found (0.0 if no relevant results).\n" +
            "- Set notes to a brief rationale explaining why no match was sufficient.\n\n" +
            "Always end your response with this exact JSON block:\n" +
            "```json\n" +
            "{\n  \"action\": \"escalate_incident\",\n  \"confidence\": 0.0,\n  \"notes\": \"...\",\n  \"matchedTicketNumber\": null\n}\n" +
            "```";

        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage(userContent)
        };
        var completionOptions = new ChatCompletionOptions { MaxOutputTokenCount = 800 };
        var chatResponse = await chatClient.CompleteChatAsync(messages, completionOptions, ct);
        string content = chatResponse.Value.Content[0].Text;
        logger.LogInformation("AI incident response for {TicketNumber}: {Content}", req.TicketNumber, content);

        var result = ParseJsonBlock<ResolutionResult>(content);

        bool autoResolved = result.Action == "incident_auto_resolved";
        string newState = autoResolved ? "Closed" : "New";
        await CallMcpToolAsync(http, mcpUrl, "update_ticket", new
        {
            ticket_id = ticketId,
            state = newState,
            resolution_notes = result.Notes,
            assigned_to = autoResolved ? "resolver-ai" : (string?)null,
            agent_action = result.Action,
            agent_confidence = result.Confidence,
            matched_ticket_number = result.MatchedTicketNumber
        }, logger, ct);

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error resolving incident {TicketNumber}", req.TicketNumber);
        return Results.Problem(ex.Message);
    }
});

app.Run();

static async Task<string> CallMcpToolAsync(HttpClient http, string mcpUrl, string toolName,
    object arguments, ILogger logger, CancellationToken ct)
{
    var payload = new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "tools/call",
        @params = new { name = toolName, arguments }
    };

    using var request = new HttpRequestMessage(HttpMethod.Post, mcpUrl)
    {
        Content = JsonContent.Create(payload)
    };
    // Required: MCP server returns 406 without both content types in Accept header
    request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");

    using HttpResponseMessage resp = await http.SendAsync(request, ct);
    resp.EnsureSuccessStatusCode();
    string body = await resp.Content.ReadAsStringAsync(ct);
    logger.LogDebug("MCP {Tool} response: {Body}", toolName, body);

    // Handle SSE format: event: message\ndata: {...}\n\n
    string jsonBody = body;
    if (body.Contains("\ndata:") || body.StartsWith("data:"))
    {
        string? dataLine = body.Split('\n')
            .FirstOrDefault(l => l.StartsWith("data:"));
        if (dataLine != null)
            jsonBody = dataLine["data:".Length..].Trim();
    }

    string? text = JsonNode.Parse(jsonBody)?["result"]?["content"]?[0]?["text"]?.GetValue<string>();
    if (string.IsNullOrEmpty(text))
        throw new InvalidOperationException($"MCP {toolName} returned no content. Body: {body}");

    return text;
}

static T ParseJsonBlock<T>(string content)
{
    int start = content.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
    if (start >= 0)
    {
        int lineStart = content.IndexOf('\n', start) + 1;
        int end = content.IndexOf("```", lineStart, StringComparison.OrdinalIgnoreCase);
        content = content[lineStart..end].Trim();
    }
    return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}

internal record ProcessRequest(string TicketNumber);
internal record ResolutionResult(string Action, double Confidence, string Notes, string? MatchedTicketNumber);
