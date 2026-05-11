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

app.MapMcp();

app.Run();
