using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace AgenticResolution.Api.Middleware;

/// <summary>
/// Middleware that authenticates admin endpoint requests using an API key.
/// Admin endpoints are disabled by default and require explicit configuration.
/// </summary>
public class AdminAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _config;
    private readonly ILogger<AdminAuthMiddleware> _logger;

    public AdminAuthMiddleware(
        RequestDelegate next,
        IConfiguration config,
        ILogger<AdminAuthMiddleware> logger)
    {
        _next = next;
        _config = config;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply to /api/admin/* paths
        if (!context.Request.Path.StartsWithSegments("/api/admin"))
        {
            await _next(context);
            return;
        }

        // Allow health check without authentication for monitoring
        if (context.Request.Path.StartsWithSegments("/api/admin/health"))
        {
            await _next(context);
            return;
        }

        // Check if admin endpoints are enabled
        bool adminEnabled = _config.GetValue<bool>("AdminEndpoints:Enabled", false);
        if (!adminEnabled)
        {
            _logger.LogWarning(
                "Admin endpoint access denied - endpoints are disabled. Path: {Path}, RemoteIp: {RemoteIp}",
                context.Request.Path,
                context.Connection.RemoteIpAddress);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                Error = "Admin endpoints are disabled",
                Message = "This endpoint requires AdminEndpoints:Enabled=true in configuration"
            });
            return;
        }

        // Validate API key
        string? configuredKey = _config["AdminEndpoints:ApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            _logger.LogError("Admin endpoints enabled but no API key configured");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                Error = "Server misconfiguration",
                Message = "Admin endpoints are enabled but no API key is configured"
            });
            return;
        }

        // Check for API key in request header
        if (!context.Request.Headers.TryGetValue("X-Admin-Api-Key", out var providedKey) ||
            string.IsNullOrWhiteSpace(providedKey))
        {
            _logger.LogWarning(
                "Admin endpoint access denied - missing API key. Path: {Path}, RemoteIp: {RemoteIp}",
                context.Request.Path,
                context.Connection.RemoteIpAddress);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.Append("WWW-Authenticate", "ApiKey");
            await context.Response.WriteAsJsonAsync(new
            {
                Error = "Authentication required",
                Message = "Admin endpoints require X-Admin-Api-Key header"
            });
            return;
        }

        // Constant-time comparison to prevent timing attacks
        if (!CryptographicEquals(providedKey!, configuredKey))
        {
            _logger.LogWarning(
                "Admin endpoint access denied - invalid API key. Path: {Path}, RemoteIp: {RemoteIp}",
                context.Request.Path,
                context.Connection.RemoteIpAddress);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                Error = "Authentication failed",
                Message = "Invalid API key"
            });
            return;
        }

        // Authentication successful - log and proceed
        _logger.LogInformation(
            "Admin endpoint access granted. Path: {Path}, RemoteIp: {RemoteIp}",
            context.Request.Path,
            context.Connection.RemoteIpAddress);

        await _next(context);
    }

    /// <summary>
    /// Constant-time string comparison to prevent timing attacks.
    /// </summary>
    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length)
            return false;

        int result = 0;
        for (int i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }
        return result == 0;
    }
}
