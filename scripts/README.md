# Deployment Scripts

This directory contains PowerShell scripts for automated deployment and data management.

## Setup-Solution.ps1

**Complete single-command setup for the entire solution.**

Performs:
- Prerequisites validation (Azure CLI, azd, .NET SDK)
- Infrastructure provisioning via `azd up`
- Database migrations
- Data reset to baseline state
- Sample data seeding (15 demo tickets and 8 KB articles)

### Usage

```powershell
# Basic setup
.\scripts\Setup-Solution.ps1

# Custom environment and location
.\scripts\Setup-Solution.ps1 -Environment "prod" -Location "westus2"

# Skip data reset (for code-only updates)
.\scripts\Setup-Solution.ps1 -SkipDataReset
```

### Parameters

- **`-Environment`**: Name of azd environment (default: current environment)
- **`-Location`**: Azure region (default: eastus2)
- **`-SkipDataReset`**: Skips data reset and seeding steps
- **`-SeedSampleTickets`**: Backward compatible flag (data seeded by default; use `-SkipDataReset` to disable)

### Prerequisites

- [Azure CLI](https://aka.ms/installazurecliwindows) authenticated (`az login`)
- [Azure Developer CLI (azd)](https://aka.ms/install-azd) installed
- .NET 8+ SDK
- User principal with **Contributor** + **User Access Administrator** roles

---

## Reset-Data.ps1

**Resets all tickets to New/unassigned baseline state via API.**

Can be run anytime, not just during deployment. Useful for:
- Resetting demo environments
- Clearing test data
- Preparing for demos or training sessions

### Usage

```powershell
# Reset using auto-discovered API URL
.\scripts\Reset-Data.ps1

# Reset with explicit API URL
.\scripts\Reset-Data.ps1 -ApiBaseUrl "https://api.example.com"

# Reset and seed 15 sample tickets
.\scripts\Reset-Data.ps1 -SeedSampleTickets

# Only seed, don't reset existing tickets
.\scripts\Reset-Data.ps1 -SeedSampleTickets -SkipReset
```

### Parameters

- **`-ApiBaseUrl`**: API base URL (auto-detects from azd environment if omitted)
- **`-SeedSampleTickets`**: Seeds 15 demo tickets (all New/unassigned)
- **`-SkipReset`**: Only seeds, doesn't reset existing tickets

### What Gets Reset

When reset is performed:
- **State**: Set to `New`
- **AssignedTo**: Set to `null` (unassigned)
- **ResolutionNotes**: Cleared
- **AgentAction**: Cleared
- **AgentConfidence**: Cleared
- **MatchedTicketNumber**: Cleared
- **Ticket Number Sequence**: Reset to 10000

Knowledge Base articles and comments are preserved.

---

## Sample Tickets

When `-SeedSampleTickets` is specified, 15 realistic demo tickets are created covering common IT support scenarios:

- Email issues (mobile access, Outlook connectivity)
- Hardware problems (printers, monitors, slow laptops)
- Network issues (VPN connectivity)
- Software requests (Adobe, SharePoint access)
- Account management (password resets, MFA setup)
- Cloud storage (OneDrive sync issues)
- Security (MFA registration)
- Teams/communication issues

All sample tickets:
- Start in **New** state
- Are **unassigned** (AssignedTo = null)
- Have realistic descriptions aligned with Knowledge Base articles
- Range in priority from Low to Critical
- Created at staggered times (10-480 minutes ago)

---

## Troubleshooting

### "Azure CLI not authenticated"
Run `az login` and ensure you're signed in with the correct account.

### "API health check failed"
Ensure the API is deployed and accessible. Check:
- API URL is correct
- API is running (`azd monitor --logs`)
- Firewall rules allow your IP
- Managed identity has Key Vault access

### "Insufficient privileges"
Your user principal needs both:
- **Contributor** role (to create resources)
- **User Access Administrator** role (to assign RBAC)

Grant these at subscription level:
```bash
az role assignment create --assignee <user@domain.com> --role "Contributor" --scope /subscriptions/<sub-id>
az role assignment create --assignee <user@domain.com> --role "User Access Administrator" --scope /subscriptions/<sub-id>
```

---

## CI/CD Usage

For automated pipelines, the setup script is designed for non-interactive use and does not require any special environment variables:

```powershell
.\scripts\Setup-Solution.ps1
```

The script will:
- Discover the current Azure CLI authenticated user as SQL Entra admin
- Provision all infrastructure with Entra-only authentication
- Configure managed identities for app access
- Seed 15 demo tickets and 8 knowledge base articles
