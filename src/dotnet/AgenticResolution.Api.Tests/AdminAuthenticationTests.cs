using System.Net;
using System.Net.Http.Json;
using System.Text;
using AgenticResolution.Api.Api;
using AgenticResolution.Api.Data;
using AgenticResolution.Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AgenticResolution.Api.Tests;

/// <summary>
/// Integration tests for admin endpoint authentication and authorization.
/// Tests middleware behavior without full database dependencies.
/// </summary>
public class AdminAuthenticationTests
{
    private HttpClient CreateTestClient(bool adminEnabled, string? apiKey)
    {
        var hostBuilder = new WebHostBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AdminEndpoints:Enabled"] = adminEnabled.ToString(),
                    ["AdminEndpoints:ApiKey"] = apiKey,
                    ["ConnectionStrings:Default"] = "Server=(localdb)\\mssqllocaldb;Database=TestDb;Trusted_Connection=True;"
                });
            })
            .ConfigureServices((context, services) =>
            {
                // Use in-memory database for tests
                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}"));
                
                services.AddScoped<ITicketNumberGenerator, TicketNumberGenerator>();
                services.AddLogging();
                services.AddRouting();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseMiddleware<AdminAuthMiddleware>();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapAdminApi();
                });
            });

        var server = new TestServer(hostBuilder);
        return server.CreateClient();
    }

    [Fact]
    public async Task ResetData_WithoutApiKey_Returns401()
    {
        // Arrange
        var client = CreateTestClient(adminEnabled: true, apiKey: "test-key-12345");
        var request = new ResetDataRequest(ResetTickets: true, SeedSampleTickets: false);

        // Act - No API key header provided
        var response = await client.PostAsJsonAsync("/api/admin/reset-data", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ResetData_WithInvalidApiKey_Returns401()
    {
        // Arrange
        var client = CreateTestClient(adminEnabled: true, apiKey: "correct-key-12345");
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", "wrong-key-99999");
        var request = new ResetDataRequest(ResetTickets: true, SeedSampleTickets: false);

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/reset-data", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_WithValidApiKey_Returns200()
    {
        // Arrange  - Health endpoint requires authentication even though auth docs say it bypasses
        var client = CreateTestClient(adminEnabled: true, apiKey: "test-key-12345");
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", "test-key-12345");

        // Act
        var response = await client.GetAsync("/api/admin/health");

        // Assert - Should return success with valid credentials
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ResetData_WhenDisabled_Returns403()
    {
        // Arrange
        var client = CreateTestClient(adminEnabled: false, apiKey: "test-key-12345");
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", "test-key-12345");
        var request = new ResetDataRequest(ResetTickets: true, SeedSampleTickets: false);

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/reset-data", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_DoesNotRequireAuthentication()
    {
        // Arrange
        var client = CreateTestClient(adminEnabled: true, apiKey: "test-key-12345");

        // Act - No API key provided
        var response = await client.GetAsync("/api/admin/health");

        // Assert - Health check should work without auth
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ApiKey_CaseSensitive_Returns401OnMismatch()
    {
        // Arrange
        var client = CreateTestClient(adminEnabled: true, apiKey: "TestKey12345");
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", "testkey12345"); // Different case
        var request = new ResetDataRequest(ResetTickets: true, SeedSampleTickets: false);

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/reset-data", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoint_EmptyApiKeyHeader_Returns401()
    {
        // Arrange
        var client = CreateTestClient(adminEnabled: true, apiKey: "test-key-12345");
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", ""); // Empty header
        var request = new ResetDataRequest(ResetTickets: true, SeedSampleTickets: false);

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/reset-data", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
