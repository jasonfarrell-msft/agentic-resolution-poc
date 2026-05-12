using TicketsApi.McpServer.Services;
using TicketsApi.McpServer.Tools;

var ticketsApiUrl = Environment.GetEnvironmentVariable("TICKETS_API_URL")
    ?? throw new InvalidOperationException("TICKETS_API_URL environment variable is required.");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<ITicketApiClient, TicketApiClientImpl>(c =>
    c.BaseAddress = new Uri(ticketsApiUrl));

builder.Services.AddHttpClient<IKbApiClient, KbApiClientImpl>(c =>
    c.BaseAddress = new Uri(ticketsApiUrl));

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<TicketTools>()
    .WithTools<KnowledgeBaseTools>();

var app = builder.Build();

// The MCP Streamable HTTP spec requires Accept: application/json, text/event-stream.
// The Python agent_framework MCPStreamableHTTPTool doesn't send this header, so we
// inject it on all MCP requests so the server doesn't reject them.
app.Use(async (context, next) =>
{
    if (!context.Request.Headers.ContainsKey("Accept") ||
        !context.Request.Headers.Accept.ToString().Contains("text/event-stream"))
    {
        context.Request.Headers["Accept"] = "application/json, text/event-stream";
    }
    await next();
});

app.MapMcp();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));

app.Run();
