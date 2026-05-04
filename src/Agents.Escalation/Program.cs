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

// /route is the original endpoint called by the incident agent
app.MapPost("/route", HandleRouteAsync);

// /process is the new endpoint matching the task spec interface
app.MapPost("/process", async (ProcessRequest req, IHttpClientFactory httpFactory, IConfiguration config,
    ILogger<Program> logger, CancellationToken ct) =>
{
    // Map new format to internal route handler
    var routeReq = new RouteRequest(
        req.TicketNumber,
        req.Confidence,
        0.8,
        req.ResolutionNotes ?? req.Action);
    return await HandleRouteInternalAsync(routeReq, httpFactory, config, logger, ct);
});

app.Run();

static async Task<IResult> HandleRouteAsync(RouteRequest req, IHttpClientFactory httpFactory,
    IConfiguration config, ILogger<Program> logger, CancellationToken ct)
    => await HandleRouteInternalAsync(req, httpFactory, config, logger, ct);

static async Task<IResult> HandleRouteInternalAsync(RouteRequest req, IHttpClientFactory httpFactory,
    IConfiguration config, ILogger<Program> logger, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(req.TicketNumber))
        return Results.BadRequest("ticketNumber is required");

    string mcpUrl = config["MCP_SERVER_URL"]
        ?? throw new InvalidOperationException("MCP_SERVER_URL not configured");
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

        string userContent =
            $"Ticket data:\r\n{ticketJson}\r\n\r\n" +
            $"Escalation context:\r\n" +
            $"Auto-resolution confidence was {req.AutoResolutionConfidence:F2} " +
            $"(below the required threshold of {req.ConfidenceThreshold:F2}).\r\n" +
            $"Reason for escalation: {req.EscalationReason ?? "Insufficient confidence for automated resolution."}";

        string systemPrompt =
            "You are an IT escalation routing agent. When an automated resolution agent could not\r\n" +
            "resolve a ticket with sufficient confidence, your job is to determine the correct\r\n" +
            "support group and individual assignee to handle it.\r\n\r\n" +
            "You are given the full ticket data (category, priority, short description, description)\r\n" +
            "and context about why automated resolution failed (low confidence, no KB match).\r\n\r\n" +
            "Available support groups and their scope:\r\n\r\n" +
            "| Group                   | Handles                                                      | Typical assignee       |\r\n" +
            "|-------------------------|--------------------------------------------------------------|------------------------|\r\n" +
            "| Network Operations      | VPN, firewall, DNS, Wi-Fi, network connectivity, proxy        | network-ops@corp       |\r\n" +
            "| Identity & Access       | Active Directory, Azure AD, MFA, SSO, password resets         | identity-team@corp     |\r\n" +
            "| End User Computing      | Laptops, desktops, printers, peripherals, OS issues, BSOD     | euc-support@corp       |\r\n" +
            "| Microsoft 365           | Outlook, Teams, SharePoint, OneDrive, Exchange, licensing     | m365-support@corp      |\r\n" +
            "| Application Support     | Line-of-business apps, ERP, CRM, business software            | app-support@corp       |\r\n" +
            "| Security Operations     | Malware, phishing, data loss, compliance, security incidents   | soc@corp               |\r\n" +
            "| Server & Infrastructure | Servers, VMs, storage, backup, data center, cloud IaaS        | infra-team@corp        |\r\n" +
            "| Database Administration | SQL Server, Oracle, database performance, backups              | dba-team@corp          |\r\n" +
            "| Service Desk Tier 2     | Complex issues not matching a specialist group                 | servicedesk-t2@corp    |\r\n" +
            "| Procurement & Assets    | Hardware purchases, asset tracking, license procurement        | procurement@corp       |\r\n\r\n" +
            "Based on the ticket content, select the BEST matching support group from the table above.\r\n" +
            "Assign to the group's typical assignee email.\r\n\r\n" +
            "Always end your response with this exact JSON block:\r\n" +
            "```json\r\n" +
            "{\r\n  \"assignedGroup\": \"...\",\r\n  \"assignedTo\": \"...\",\r\n  \"rationale\": \"...\",\r\n  \"confidence\": 0.0\r\n}\r\n" +
            "```\r\n\r\n" +
            "Where:\r\n" +
            "- assignedGroup: the Group name from the table (exact string)\r\n" +
            "- assignedTo: the Typical assignee email from the table\r\n" +
            "- rationale: 1-2 sentences explaining why this group was chosen\r\n" +
            "- confidence: your confidence in this routing decision (0.0-1.0)";

        // Build Azure OpenAI client for the configured deployment
        string modelDeployment = "gpt-41-mini";
        var azureAiClient = new AzureOpenAIClient(new Uri(aiEndpoint), credential);
        var chatClient = azureAiClient.GetChatClient(modelDeployment);
        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage(userContent)
        };
        var completionOptions = new ChatCompletionOptions { MaxOutputTokenCount = 600 };
        var chatResponse = await chatClient.CompleteChatAsync(messages, completionOptions, ct);
        string content = chatResponse.Value.Content[0].Text;
        logger.LogInformation("Escalation routing for {TicketNumber}: {Content}", req.TicketNumber, content);

        var routing = ParseJsonBlock<RoutingResult>(content);

        await CallMcpToolAsync(http, mcpUrl, "update_ticket", new
        {
            ticket_id = ticketId,
            state = "InProgress",
            resolution_notes = $"Escalated to {routing.AssignedGroup}: {routing.Rationale}",
            assigned_to = routing.AssignedTo,
            agent_action = "escalation_routed",
            agent_confidence = routing.Confidence
        }, logger, ct);

        logger.LogInformation("Ticket {TicketNumber} routed to {Group} ({AssignedTo})",
            req.TicketNumber, routing.AssignedGroup, routing.AssignedTo);

        // Return in both formats for compatibility
        var response = new
        {
            escalationAction = routing.AssignedGroup,
            assignee = routing.AssignedTo,
            notes = routing.Rationale,
            confidence = routing.Confidence,
            // Legacy fields
            assignedGroup = routing.AssignedGroup,
            assignedTo = routing.AssignedTo,
            rationale = routing.Rationale
        };
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error routing ticket {TicketNumber}", req.TicketNumber);
        return Results.Problem(ex.Message);
    }
}

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

// Original format from incident agent
internal record RouteRequest(string TicketNumber, double AutoResolutionConfidence,
    double ConfidenceThreshold, string? EscalationReason);

// New /process format from task spec
internal record ProcessRequest(string TicketNumber, double Confidence,
    string? ResolutionNotes, string? Action);

internal record RoutingResult(string AssignedGroup, string AssignedTo, string Rationale, double Confidence);
