using AgenticResolution.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AgenticResolution.Api.Data;

public class AppDbContext : DbContext
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketNumberSequence> TicketNumberSequences => Set<TicketNumberSequence>();
    public DbSet<TicketComment> Comments => Set<TicketComment>();
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var ticket = modelBuilder.Entity<Ticket>();
        ticket.HasKey(t => t.Id);
        ticket.Property(t => t.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        ticket.Property(t => t.Number).IsRequired().HasMaxLength(15);
        ticket.HasIndex(t => t.Number).IsUnique();
        ticket.HasIndex(t => t.State);
        ticket.HasIndex(t => t.CreatedAt);
        ticket.Property(t => t.ShortDescription).IsRequired().HasMaxLength(200);
        ticket.Property(t => t.Category).IsRequired().HasMaxLength(100);
        ticket.Property(t => t.Caller).IsRequired().HasMaxLength(100);
        ticket.Property(t => t.AssignedTo).HasMaxLength(100);
        ticket.Property(t => t.Priority).HasConversion<int>();
        ticket.Property(t => t.State).HasConversion<int>();
        ticket.Property(t => t.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        ticket.Property(t => t.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        ticket.Property(t => t.AgentAction).HasMaxLength(100);
        ticket.Property(t => t.MatchedTicketNumber).HasMaxLength(20);
        ticket.Property(t => t.Classification).HasMaxLength(20);
        ticket.HasIndex(t => t.UpdatedAt);
        ticket.HasIndex(t => t.AssignedTo);
        ticket.HasIndex(t => t.Category);

        var comment = modelBuilder.Entity<TicketComment>();
        comment.HasKey(c => c.Id);
        comment.Property(c => c.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        comment.Property(c => c.Author).IsRequired().HasMaxLength(100);
        comment.Property(c => c.Body).IsRequired();
        comment.Property(c => c.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        comment.HasOne(c => c.Ticket)
            .WithMany()
            .HasForeignKey(c => c.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
        comment.HasIndex(c => c.TicketId);

        var seq = modelBuilder.Entity<TicketNumberSequence>();
        seq.HasKey(s => s.Id);
        seq.Property(s => s.Id).ValueGeneratedNever();
        seq.HasData(new TicketNumberSequence { Id = 1, LastValue = 10000L });

        var article = modelBuilder.Entity<KnowledgeArticle>();
        article.HasKey(a => a.Id);
        article.Property(a => a.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        article.Property(a => a.Number).IsRequired().HasMaxLength(20);
        article.HasIndex(a => a.Number).IsUnique();
        article.Property(a => a.Title).IsRequired().HasMaxLength(300);
        article.Property(a => a.Category).IsRequired().HasMaxLength(100);
        article.Property(a => a.Author).IsRequired().HasMaxLength(100);
        article.Property(a => a.Tags).HasMaxLength(500);
        article.Property(a => a.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        article.Property(a => a.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        article.HasIndex(a => a.Category);
        article.HasIndex(a => a.IsPublished);
        article.Property(a => a.IsPublished).HasDefaultValue(true);
        article.HasData(GetSeedArticles());
    }

    private static KnowledgeArticle[] GetSeedArticles() =>
    [
        new KnowledgeArticle
        {
            Id = new Guid("a1000001-0000-0000-0000-000000000000"),
            Number = "KB0001001",
            Title = "How to Reset Your Password",
            Body = "Forgot your password? Navigate to the Microsoft 365 login page and click 'Can't access your account?' to begin the self-service reset process. You will need to verify your identity using your registered mobile phone number or alternate email address. New passwords must be at least 12 characters long and include uppercase letters, lowercase letters, numbers, and special characters. Your previous 10 passwords cannot be reused, and passwords expire every 90 days. If you are locked out and cannot complete self-service reset, contact the IT Help Desk immediately.",
            Category = "Account Management",
            Author = "IT Support Team",
            Tags = "password,reset,account,login,credentials",
            ViewCount = 487,
            IsPublished = true,
            CreatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)
        },
        new KnowledgeArticle
        {
            Id = new Guid("a1000002-0000-0000-0000-000000000000"),
            Number = "KB0001002",
            Title = "VPN Setup Guide for Remote Workers",
            Body = "The corporate VPN allows remote employees to securely access internal systems and network resources from outside the office. Download the GlobalProtect VPN client from the IT Software Portal using your corporate credentials and follow the installation wizard. After installation, enter the gateway address 'vpn.company.com' and authenticate with your Active Directory username, password, and MFA code. The client will automatically reconnect if your connection is interrupted during a session. If you experience persistent connection issues, ensure your local firewall is not blocking UDP port 4501 and contact the Network Team.",
            Category = "Network",
            Author = "Network Team",
            Tags = "vpn,remote,network,globalprotect,remote work",
            ViewCount = 312,
            IsPublished = true,
            CreatedAt = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc)
        },
        new KnowledgeArticle
        {
            Id = new Guid("a1000003-0000-0000-0000-000000000000"),
            Number = "KB0001003",
            Title = "Outlook Not Connecting: Troubleshooting Steps",
            Body = "If Outlook displays 'Disconnected' or 'Trying to connect' in the status bar, first verify your internet connection and ensure VPN is active if you are working remotely. Run the Microsoft Support and Recovery Assistant (SARA) tool, available from the IT portal, to automatically detect and fix common Outlook issues without manual intervention. Closing and reopening Outlook, or restarting the Microsoft Office service, resolves most temporary disconnection issues caused by network interruptions. If your mailbox has exceeded its storage limit, archive older emails to a local PST file to restore connectivity. Persistent issues may indicate Exchange profile corruption — submit a ticket to IT Support for profile recreation.",
            Category = "Email",
            Author = "IT Support Team",
            Tags = "outlook,email,connectivity,troubleshooting,office365",
            ViewCount = 445,
            IsPublished = true,
            CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        },
        new KnowledgeArticle
        {
            Id = new Guid("a1000004-0000-0000-0000-000000000000"),
            Number = "KB0001004",
            Title = "How to Request Software Installation",
            Body = "All software installations must be requested through the IT Service Portal to maintain license compliance and security standards across the organization. Log in to the portal, navigate to 'Request Software', and select from over 200 pre-approved applications in the software catalog. Non-catalog software requires a completed business justification form and manager approval, which typically takes 3 to 5 business days to process. Once approved, software is deployed to your device automatically via Microsoft Intune — no action is required on your part. For urgent requests or questions about the approval process, call the Help Desk at extension 5000.",
            Category = "Software",
            Author = "IT Support Team",
            Tags = "software,installation,request,portal,approved,intune",
            ViewCount = 263,
            IsPublished = true,
            CreatedAt = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc)
        },
        new KnowledgeArticle
        {
            Id = new Guid("a1000005-0000-0000-0000-000000000000"),
            Number = "KB0001005",
            Title = "Printer Setup and Troubleshooting",
            Body = "Network printers are available to all employees connected to the corporate network or VPN. Open Settings, select 'Printers & Scanners', and click 'Add a printer or scanner' — your office printer should appear automatically on the correct network segment. If the printer is not discovered automatically, click 'The printer that I want isn't listed' and enter the printer's IP address, which is printed on the device's configuration page. Install the manufacturer's drivers from the IT portal if prompted, as unsigned drivers are blocked by group policy. For hardware problems such as persistent paper jams, error lights, or poor print quality, submit a Desktop Support ticket with the printer's asset tag number visible on the device label.",
            Category = "Hardware",
            Author = "Desktop Support",
            Tags = "printer,hardware,driver,network,print,setup",
            ViewCount = 178,
            IsPublished = true,
            CreatedAt = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc)
        },
        new KnowledgeArticle
        {
            Id = new Guid("a1000006-0000-0000-0000-000000000000"),
            Number = "KB0001006",
            Title = "Multi-Factor Authentication Setup",
            Body = "Multi-factor authentication (MFA) is mandatory for all employees and protects your corporate account from unauthorized access even if your password is compromised. Download the Microsoft Authenticator app from the Apple App Store or Google Play Store on your smartphone before beginning enrollment. Visit https://aka.ms/MFASetup while signed in with your corporate account and follow the on-screen steps to register your device by scanning the QR code displayed. When signing in to Microsoft 365 and other corporate applications, you will be prompted to approve the sign-in request in the Authenticator app within 30 seconds. If you lose your phone or change devices, contact the Security Team immediately to reset your MFA registration and prevent unauthorized access.",
            Category = "Security",
            Author = "Security Team",
            Tags = "mfa,authentication,security,authenticator,2fa,identity",
            ViewCount = 389,
            IsPublished = true,
            CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        },
        new KnowledgeArticle
        {
            Id = new Guid("a1000007-0000-0000-0000-000000000000"),
            Number = "KB0001007",
            Title = "OneDrive Sync Issues: Common Fixes",
            Body = "OneDrive for Business automatically syncs files between your device and the cloud, ensuring your work is backed up and accessible from anywhere. If files are not syncing, check the OneDrive cloud icon in the system tray for error messages or warning icons indicating the specific problem. Common causes of sync failures include file names with unsupported characters, file paths exceeding 400 characters, or reaching your storage quota limit. Pausing and resuming sync from the OneDrive tray menu often resolves transient issues caused by network interruptions. If a specific file shows a persistent red X error, save it to a different location and re-upload it manually, then delete the original problematic file.",
            Category = "Cloud Storage",
            Author = "IT Support Team",
            Tags = "onedrive,sync,cloud,storage,files,microsoft365",
            ViewCount = 201,
            IsPublished = true,
            CreatedAt = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc)
        },
        new KnowledgeArticle
        {
            Id = new Guid("a1000008-0000-0000-0000-000000000000"),
            Number = "KB0001008",
            Title = "How to Submit a Help Desk Ticket",
            Body = "The IT Help Desk portal is your single point of contact for all technology-related issues and service requests within the organization. To submit a new ticket, navigate to https://itportal.company.com, select 'New Incident' for break-fix issues or 'New Request' for service requests and software access. Provide as much detail as possible — including the exact error message, affected application, your device name, and steps already taken — to help our team resolve the issue faster. You can track your ticket status, add follow-up comments, and view resolution notes through the portal at any time using your corporate credentials. For critical incidents affecting multiple users or core business operations, call the Help Desk directly at extension 5000 to ensure immediate escalation to senior engineers.",
            Category = "Getting Help",
            Author = "IT Support Team",
            Tags = "help desk,ticket,support,portal,request,incident",
            ViewCount = 156,
            IsPublished = true,
            CreatedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        }
    ];
}

public interface ITicketNumberGenerator
{
    Task<string> NextAsync(CancellationToken ct = default);
}

public class TicketNumberGenerator : ITicketNumberGenerator
{
    private readonly AppDbContext _db;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public TicketNumberGenerator(AppDbContext db) => _db = db;

    public async Task<string> NextAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            long value = (await _db.Database
                .SqlQueryRaw<long>(
                    "UPDATE TicketNumberSequences SET LastValue = LastValue + 1 OUTPUT INSERTED.LastValue AS Value WHERE Id = 1",
                    Array.Empty<object>())
                .ToListAsync(ct))
                .FirstOrDefault();

            if (value == 0L)
            {
                var row = await _db.TicketNumberSequences.FirstAsync(s => s.Id == 1, ct);
                row.LastValue++;
                await _db.SaveChangesAsync(ct);
                value = row.LastValue;
            }
            return $"INC{value:D7}";
        }
        finally
        {
            _gate.Release();
        }
    }
}
