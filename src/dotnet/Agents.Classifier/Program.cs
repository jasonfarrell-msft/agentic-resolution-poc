using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.AI.Inference;
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks();
builder.Services.AddApplicationInsightsTelemetry();

var app = builder.Build();
app.MapHealthChecks("/health");

const string ClassificationPrompt = """
    You are an IT ticket classification agent. Your job is to read a ticket and determine
    whether it is an Incident or a Service Request.

    An Incident is something that is broken, degraded, failing, or causing harm to the user
    or business - e.g., "my laptop won't start", "I can't access the VPN", "application is down".

    A Service Request is something the user wants or needs that is not broken - e.g.,
    "I need a new laptop", "please install Photoshop", "grant me access to SharePoint".

    Always end your response with this exact JSON block:
    ```json
    {
      "classification": "incident",
      "confidence": 0.0,
      "rationale": "..."
    }
    ```
    Replace "incident" with "request" if applicable.
    """;

app.MapPost("/process", async (
    ProcessRequest req,
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.TicketNumber))
        return Results.BadRequest("ticketNumber is required");

    var mcpUrl = config["MCP_SERVER_URL"]
        ?? "https://ca-mcp-ie6eryvrpccqa.greenground-d61ff22e.eastus2.azurecontainerapps.io/mcp";
    var aiEndpoint = config["AZURE_AI_ENDPOINT"]
        ?? throw new InvalidOperationException("AZURE_AI_ENDPOINT not configured");
    var clientId = config["AZURE_CLIENT_ID"];

    Azure.Core.TokenCredential credential = string.IsNullOrWhiteSpace(clientId)
        ? new DefaultAzureCredential()
        : new ManagedIdentityCredential(clientId);

    var http = httpFactory.CreateClient();

    try
    {
        var ticketJson = await CallMcpToolAsync(http, mcpUrl, "get_ticket_by_number",
            new { ticket_number = req.TicketNumber }, logger, ct);

        var chatClient = new ChatCompletionsClient(new Uri(aiEndpoint), credential);
        var response = await chatClient.CompleteAsync(new ChatCompletionsOptions
        {
            Model = "gpt-41-mini",
            Messages =
            {
                new ChatRequestSystemMessage(ClassificationPrompt),
                new ChatRequestUserMessage($"Classify this ticket:\n{ticketJson}")
            },
            MaxTokens = 400
        }, ct);

        var content = response.Value.Content;
        logger.LogInformation("AI classification for {TicketNumber}: {Content}", req.TicketNumber, content);

        var result = ParseJsonBlock<ClassificationResult>(content);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error classifying ticket {TicketNumber}", req.TicketNumber);
        return Results.Problem(ex.Message);
    }
});

app.Run();

static async Task<string> CallMcpToolAsync(
    HttpClient http, string mcpUrl, string toolName, object arguments,
    ILogger logger, CancellationToken ct)
{
    var rpcRequest = new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "tools/call",
        @params = new { name = toolName, arguments }
    };

    using var resp = await http.PostAsJsonAsync(mcpUrl, rpcRequest, ct);
    resp.EnsureSuccessStatusCode();

    var body = await resp.Content.ReadAsStringAsync(ct);
    logger.LogDebug("MCP {Tool} response: {Body}", toolName, body);

    var node = JsonNode.Parse(body);
    var text = node?["result"]?["content"]?[0]?["text"]?.GetValue<string>();

    if (string.IsNullOrEmpty(text))
        throw new InvalidOperationException($"MCP {toolName} returned no content. Body: {body}");

    return text;
}

static T ParseJsonBlock<T>(string content)
{
    var fenceStart = content.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
    if (fenceStart >= 0)
    {
        var jsonStart = content.IndexOf('\n', fenceStart) + 1;
        var fenceEnd = content.IndexOf("```", jsonStart, StringComparison.OrdinalIgnoreCase);
        content = content[jsonStart..fenceEnd].Trim();
    }
    return JsonSerializer.Deserialize<T>(content,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}

record ProcessRequest(string TicketNumber);
record ClassificationResult(string Classification, double Confidence, string Rationale);

