using AgenticResolution.Api.Agents;
using AgenticResolution.Api.Api;
using AgenticResolution.Api.Data;
using AgenticResolution.Api.Webhooks;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(opts =>
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddApplicationInsightsTelemetry();

string? kvUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(kvUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(kvUri), new DefaultAzureCredential(),
        new AzureKeyVaultConfigurationOptions());
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddHttpClient("agents");
builder.Services.AddScoped<AgentOrchestrationService>();
builder.Services.AddScoped<ITicketNumberGenerator, TicketNumberGenerator>();
builder.Services.AddSingleton<IWebhookDispatcher, WebhookDispatcher>();
builder.Services.AddHttpClient("webhook");
builder.Services.AddHostedService<WebhookDispatchService>();

builder.Services.AddCors(options =>
    options.AddPolicy("BlazorFrontend", policy =>
    {
        string[] origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:5000", "https://localhost:7000"];
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    }));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    string connStr = builder.Configuration.GetConnectionString("Default") ?? string.Empty;
    if (!connStr.Contains("(placeholder)", StringComparison.OrdinalIgnoreCase))
        await db.Database.MigrateAsync();
}

app.UseCors("BlazorFrontend");
app.UseHttpsRedirection();
app.MapTicketsApi();
app.Run();
