using AgenticResolution.Web.Components;
using AgenticResolution.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(options =>
    {
        options.DetailedErrors = builder.Environment.IsDevelopment();
    })
    .AddHubOptions(options =>
    {
        options.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    });
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowAnyOrigin());
});
builder.Services.AddHttpClient<TicketApiClient>(client =>
{
    var baseUrl = FirstConfigured(
        builder.Configuration["TICKETS_API_URL"],
        builder.Configuration["ApiBaseUrl"],
        builder.Configuration["ApiClient:BaseUrl"]);
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        client.BaseAddress = new Uri(baseUrl.Trim(), UriKind.Absolute);
    }
});
builder.Services.AddHttpClient<ResolutionApiClient>(client =>
{
    var baseUrl = builder.Configuration["ResolutionApi:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        client.BaseAddress = new Uri(baseUrl.Trim(), UriKind.Absolute);
    }
    client.Timeout = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseCors();

app.UseAntiforgery();

app.MapMethods("/api/{**path}", ["GET", "POST", "PUT", "PATCH", "DELETE"], (string? path) =>
    Results.Problem(
        title: "API endpoint is not hosted by this web app.",
        detail: $"Use the configured tickets API base URL for /api/{path}.",
        statusCode: StatusCodes.Status404NotFound));

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string? FirstConfigured(params string?[] values) =>
    values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
