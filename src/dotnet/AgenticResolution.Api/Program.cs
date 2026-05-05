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
    try
    {
        builder.Configuration.AddAzureKeyVault(new Uri(kvUri), new DefaultAzureCredential(),
            new AzureKeyVaultConfigurationOptions());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Startup] Key Vault configuration failed (non-fatal): {ex.Message}");
    }
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"))
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddHttpClient("agents");
builder.Services.AddScoped<AgentOrchestrationService>();
builder.Services.AddScoped<IWorkflowProgressTracker, WorkflowProgressTracker>();
builder.Services.AddScoped<ITicketNumberGenerator, TicketNumberGenerator>();
builder.Services.AddSingleton<IResolutionQueue, ResolutionQueue>();
builder.Services.AddHostedService<ResolutionRunnerService>();
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
    {
        try
        {
            Console.WriteLine("[Startup] Running database migrations...");
            var pending = await db.Database.GetPendingMigrationsAsync();
            Console.WriteLine($"[Startup] Pending migrations: {string.Join(", ", pending)}");
            await db.Database.MigrateAsync();
            Console.WriteLine("[Startup] Database migrations completed.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Startup] Migration failed: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            throw;
        }

        // Idempotent fallback: ensure KnowledgeArticles table exists even if migration history is inconsistent.
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'KnowledgeArticles')
BEGIN
    CREATE TABLE [KnowledgeArticles] (
        [Id] uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID(),
        [Number] nvarchar(20) NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [Category] nvarchar(100) NOT NULL,
        [Author] nvarchar(100) NOT NULL,
        [Tags] nvarchar(500) NULL,
        [ViewCount] int NOT NULL DEFAULT 0,
        [IsPublished] bit NOT NULL DEFAULT 1,
        [CreatedAt] datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [UpdatedAt] datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [PK_KnowledgeArticles] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_KnowledgeArticles_Number] ON [KnowledgeArticles] ([Number]);
    CREATE INDEX [IX_KnowledgeArticles_Category] ON [KnowledgeArticles] ([Category]);
    CREATE INDEX [IX_KnowledgeArticles_IsPublished] ON [KnowledgeArticles] ([IsPublished]);

    INSERT INTO [KnowledgeArticles] ([Id],[Number],[Title],[Body],[Category],[Author],[Tags],[ViewCount],[IsPublished],[CreatedAt],[UpdatedAt]) VALUES ('a1000001-0000-0000-0000-000000000000','KB0001001','How to Reset Your Password','Forgot your password? Navigate to the Microsoft 365 login page and click ''Can''t access your account?'' to begin the self-service reset process. You will need to verify your identity using your registered mobile phone number or alternate email address. New passwords must be at least 12 characters long and include uppercase letters, lowercase letters, numbers, and special characters. Your previous 10 passwords cannot be reused, and passwords expire every 90 days. If you are locked out and cannot complete self-service reset, contact the IT Help Desk immediately.','Account Management','IT Support Team','password,reset,account,login,credentials',487,1,'2026-01-15','2026-01-15');
    INSERT INTO [KnowledgeArticles] ([Id],[Number],[Title],[Body],[Category],[Author],[Tags],[ViewCount],[IsPublished],[CreatedAt],[UpdatedAt]) VALUES ('a1000002-0000-0000-0000-000000000000','KB0001002','VPN Setup Guide for Remote Workers','The corporate VPN allows remote employees to securely access internal systems and network resources from outside the office. Download the GlobalProtect VPN client from the IT Software Portal using your corporate credentials and follow the installation wizard. After installation, enter the gateway address ''vpn.company.com'' and authenticate with your Active Directory username, password, and MFA code. The client will automatically reconnect if your connection is interrupted during a session. If you experience persistent connection issues, ensure your local firewall is not blocking UDP port 4501 and contact the Network Team.','Network','Network Team','vpn,remote,network,globalprotect,remote work',312,1,'2026-01-22','2026-01-22');
    INSERT INTO [KnowledgeArticles] ([Id],[Number],[Title],[Body],[Category],[Author],[Tags],[ViewCount],[IsPublished],[CreatedAt],[UpdatedAt]) VALUES ('a1000003-0000-0000-0000-000000000000','KB0001003','Outlook Not Connecting: Troubleshooting Steps','If Outlook displays ''Disconnected'' or ''Trying to connect'' in the status bar, first verify your internet connection and ensure VPN is active if you are working remotely. Run the Microsoft Support and Recovery Assistant (SARA) tool, available from the IT portal, to automatically detect and fix common Outlook issues without manual intervention. Closing and reopening Outlook, or restarting the Microsoft Office service, resolves most temporary disconnection issues caused by network interruptions. If your mailbox has exceeded its storage limit, archive older emails to a local PST file to restore connectivity. Persistent issues may indicate Exchange profile corruption — submit a ticket to IT Support for profile recreation.','Email','IT Support Team','outlook,email,connectivity,troubleshooting,office365',445,1,'2026-02-01','2026-02-01');
    INSERT INTO [KnowledgeArticles] ([Id],[Number],[Title],[Body],[Category],[Author],[Tags],[ViewCount],[IsPublished],[CreatedAt],[UpdatedAt]) VALUES ('a1000004-0000-0000-0000-000000000000','KB0001004','How to Request Software Installation','All software installations must be requested through the IT Service Portal to maintain license compliance and security standards across the organization. Log in to the portal, navigate to ''Request Software'', and select from over 200 pre-approved applications in the software catalog. Non-catalog software requires a completed business justification form and manager approval, which typically takes 3 to 5 business days to process. Once approved, software is deployed to your device automatically via Microsoft Intune — no action is required on your part. For urgent requests or questions about the approval process, call the Help Desk at extension 5000.','Software','IT Support Team','software,installation,request,portal,approved,intune',263,1,'2026-02-10','2026-02-10');
    INSERT INTO [KnowledgeArticles] ([Id],[Number],[Title],[Body],[Category],[Author],[Tags],[ViewCount],[IsPublished],[CreatedAt],[UpdatedAt]) VALUES ('a1000005-0000-0000-0000-000000000000','KB0001005','Printer Setup and Troubleshooting','Network printers are available to all employees connected to the corporate network or VPN. Open Settings, select ''Printers & Scanners'', and click ''Add a printer or scanner'' — your office printer should appear automatically on the correct network segment. If the printer is not discovered automatically, click ''The printer that I want isn''t listed'' and enter the printer''s IP address, which is printed on the device''s configuration page. Install the manufacturer''s drivers from the IT portal if prompted, as unsigned drivers are blocked by group policy. For hardware problems such as persistent paper jams, error lights, or poor print quality, submit a Desktop Support ticket with the printer''s asset tag number visible on the device label.','Hardware','Desktop Support','printer,hardware,driver,network,print,setup',178,1,'2026-02-20','2026-02-20');
    INSERT INTO [KnowledgeArticles] ([Id],[Number],[Title],[Body],[Category],[Author],[Tags],[ViewCount],[IsPublished],[CreatedAt],[UpdatedAt]) VALUES ('a1000006-0000-0000-0000-000000000000','KB0001006','Multi-Factor Authentication Setup','Multi-factor authentication (MFA) is mandatory for all employees and protects your corporate account from unauthorized access even if your password is compromised. Download the Microsoft Authenticator app from the Apple App Store or Google Play Store on your smartphone before beginning enrollment. Visit https://aka.ms/MFASetup while signed in with your corporate account and follow the on-screen steps to register your device by scanning the QR code displayed. When signing in to Microsoft 365 and other corporate applications, you will be prompted to approve the sign-in request in the Authenticator app within 30 seconds. If you lose your phone or change devices, contact the Security Team immediately to reset your MFA registration and prevent unauthorized access.','Security','Security Team','mfa,authentication,security,authenticator,2fa,identity',389,1,'2026-03-01','2026-03-01');
    INSERT INTO [KnowledgeArticles] ([Id],[Number],[Title],[Body],[Category],[Author],[Tags],[ViewCount],[IsPublished],[CreatedAt],[UpdatedAt]) VALUES ('a1000007-0000-0000-0000-000000000000','KB0001007','OneDrive Sync Issues: Common Fixes','OneDrive for Business automatically syncs files between your device and the cloud, ensuring your work is backed up and accessible from anywhere. If files are not syncing, check the OneDrive cloud icon in the system tray for error messages or warning icons indicating the specific problem. Common causes of sync failures include file names with unsupported characters, file paths exceeding 400 characters, or reaching your storage quota limit. Pausing and resuming sync from the OneDrive tray menu often resolves transient issues caused by network interruptions. If a specific file shows a persistent red X error, save it to a different location and re-upload it manually, then delete the original problematic file.','Cloud Storage','IT Support Team','onedrive,sync,cloud,storage,files,microsoft365',201,1,'2026-03-15','2026-03-15');
    INSERT INTO [KnowledgeArticles] ([Id],[Number],[Title],[Body],[Category],[Author],[Tags],[ViewCount],[IsPublished],[CreatedAt],[UpdatedAt]) VALUES ('a1000008-0000-0000-0000-000000000000','KB0001008','How to Submit a Help Desk Ticket','The IT Help Desk portal is your single point of contact for all technology-related issues and service requests within the organization. To submit a new ticket, navigate to https://itportal.company.com, select ''New Incident'' for break-fix issues or ''New Request'' for service requests and software access. Provide as much detail as possible — including the exact error message, affected application, your device name, and steps already taken — to help our team resolve the issue faster. You can track your ticket status, add follow-up comments, and view resolution notes through the portal at any time using your corporate credentials. For critical incidents affecting multiple users or core business operations, call the Help Desk directly at extension 5000 to ensure immediate escalation to senior engineers.','Getting Help','IT Support Team','help desk,ticket,support,portal,request,incident',156,1,'2026-04-01','2026-04-01');

    PRINT 'KnowledgeArticles table created and seeded.';
END
");
            Console.WriteLine("[Startup] KnowledgeArticles table check completed.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Startup] KnowledgeArticles fallback failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

app.UseCors("BlazorFrontend");
app.UseHttpsRedirection();
app.MapTicketsApi();
app.MapKnowledgeBaseApi();
app.Run();
