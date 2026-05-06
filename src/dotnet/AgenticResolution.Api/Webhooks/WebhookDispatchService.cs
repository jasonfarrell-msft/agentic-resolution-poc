using AgenticResolution.Api.Models;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace AgenticResolution.Api.Webhooks;

public interface IWebhookDispatcher
{
    ChannelReader<WebhookEnvelope> Reader { get; }
    bool Enqueue(WebhookPayload payload);
}

public class WebhookDispatcher : IWebhookDispatcher
{
    private readonly Channel<WebhookEnvelope> _channel;
    private readonly ILogger<WebhookDispatcher> _logger;

    public ChannelReader<WebhookEnvelope> Reader => _channel.Reader;

    public WebhookDispatcher(ILogger<WebhookDispatcher> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<WebhookEnvelope>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool Enqueue(WebhookPayload payload)
    {
        bool ok = _channel.Writer.TryWrite(new WebhookEnvelope(payload));
        if (!ok)
            _logger.LogWarning("Webhook channel rejected payload {EventId}", payload.EventId);
        else
            _logger.LogInformation("Webhook enqueued {EventType} {EventId}", payload.EventType, payload.EventId);
        return ok;
    }
}

public class WebhookOptions
{
    public string? TargetUrl { get; set; }
    public string? Secret { get; set; }
}

public class WebhookDispatchService : BackgroundService
{
    private static readonly TimeSpan[] _backoff =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(16)];

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    private readonly IWebhookDispatcher _dispatcher;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhookDispatchService> _logger;

    public WebhookDispatchService(IWebhookDispatcher dispatcher, IHttpClientFactory httpFactory,
        IConfiguration config, IServiceScopeFactory scopeFactory, ILogger<WebhookDispatchService> logger)
    {
        _dispatcher = dispatcher;
        _httpFactory = httpFactory;
        _config = config;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var envelope in _dispatcher.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DispatchWithRetryAsync(envelope, stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook dispatch failed permanently for {EventId}", envelope.Payload.EventId);
            }
        }
    }

    private async Task DispatchWithRetryAsync(WebhookEnvelope envelope, CancellationToken ct)
    {
        string? target = _config["Webhook:TargetUrl"];
        string? secret = _config["Webhook:Secret"];
        if (string.IsNullOrWhiteSpace(target))
        {
            _logger.LogWarning("Webhook:TargetUrl not configured; dropping {EventId}", envelope.Payload.EventId);
            return;
        }

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(envelope.Payload, _json);
        string signature = ComputeSignature(body, secret);

        for (int attempt = 1; attempt <= _backoff.Length + 1; attempt++)
        {
            try
            {
                using var client = _httpFactory.CreateClient("webhook");
                client.Timeout = TimeSpan.FromSeconds(15);
                using var content = new ByteArrayContent(body);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var req = new HttpRequestMessage(HttpMethod.Post, target) { Content = content };
                req.Headers.TryAddWithoutValidation("X-Resolution-Signature", "sha256=" + signature);
                req.Headers.TryAddWithoutValidation("X-Resolution-Event-Id", envelope.Payload.EventId.ToString());
                req.Headers.TryAddWithoutValidation("X-Resolution-Event-Type", envelope.Payload.EventType);
                using var resp = await client.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Webhook delivered {EventId} attempt={Attempt} status={Status}",
                        envelope.Payload.EventId, attempt, (int)resp.StatusCode);
                    return;
                }
                _logger.LogWarning("Webhook attempt {Attempt} for {EventId} returned {Status}",
                    attempt, envelope.Payload.EventId, (int)resp.StatusCode);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Webhook attempt {Attempt} for {EventId} threw", attempt, envelope.Payload.EventId);
            }

            if (attempt <= _backoff.Length)
            {
                try { await Task.Delay(_backoff[attempt - 1], ct); }
                catch (OperationCanceledException) { throw; }
            }
        }
        _logger.LogError("Webhook gave up after {Attempts} attempts for {EventId}",
            _backoff.Length + 1, envelope.Payload.EventId);
    }

    private static string ComputeSignature(byte[] body, string? secret)
    {
        byte[] key = Encoding.UTF8.GetBytes(secret ?? string.Empty);
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }
}

public record TicketWebhookSnapshot(string Number, string ShortDescription, string Category,
    string Priority, string Urgency, string Impact, string State, string Caller,
    string? AssignmentGroup, DateTime OpenedAt)
{
    public static TicketWebhookSnapshot From(Ticket t) =>
        new(t.Number, t.ShortDescription, t.Category,
            ((int)t.Priority).ToString(), ((int)t.Priority).ToString(), ((int)t.Priority).ToString(),
            t.State.ToString(), t.Caller, null, t.CreatedAt);
}

public record WebhookPayload(Guid EventId, string EventType, DateTime Timestamp, TicketWebhookSnapshot Ticket, Guid? RunId = null, string? ErrorMessage = null)
{
    public static WebhookPayload ForTicketCreated(Ticket t) =>
        new(Guid.NewGuid(), "ticket.created", DateTime.UtcNow, TicketWebhookSnapshot.From(t));

    public static WebhookPayload ForTicketUpdated(Ticket t) =>
        new(Guid.NewGuid(), "ticket.updated", DateTime.UtcNow, TicketWebhookSnapshot.From(t));

    public static WebhookPayload ForResolutionStarted(Ticket t, Guid runId) =>
        new(Guid.NewGuid(), "resolution.started", DateTime.UtcNow, TicketWebhookSnapshot.From(t), runId);

    public static WebhookPayload ForWorkflowRunning(Ticket t, Guid runId) =>
        new(Guid.NewGuid(), "workflow.running", DateTime.UtcNow, TicketWebhookSnapshot.From(t), runId);

    public static WebhookPayload ForWorkflowCompleted(Ticket t, Guid runId) =>
        new(Guid.NewGuid(), "workflow.completed", DateTime.UtcNow, TicketWebhookSnapshot.From(t), runId);

    public static WebhookPayload ForWorkflowEscalated(Ticket t, Guid runId) =>
        new(Guid.NewGuid(), "workflow.escalated", DateTime.UtcNow, TicketWebhookSnapshot.From(t), runId);

    public static WebhookPayload ForWorkflowFailed(Ticket t, Guid runId, string errorMessage) =>
        new(Guid.NewGuid(), "workflow.failed", DateTime.UtcNow, TicketWebhookSnapshot.From(t), runId, errorMessage);
}

public record WebhookEnvelope(WebhookPayload Payload, int Attempt = 0);
