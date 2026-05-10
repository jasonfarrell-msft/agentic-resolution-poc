# Deployment Scripts

This directory contains PowerShell scripts for automated deployment and data management.

## Setup-Solution.ps1

**Complete single-command setup for the entire solution.**

Performs:
- Prerequisites validation (Azure CLI, azd, .NET SDK)
- Infrastructure provisioning via `azd up`
- Database migrations
- Data reset to baseline state
- Optional sample data seeding

### Usage

```powershell
# Basic setup
.\scripts\Setup-Solution.ps1

# Setup with sample demo tickets
.\scripts\Setup-Solution.ps1 -SeedSampleTickets

# CI/CD with environment variable password
$env:SQL_ADMIN_PASSWORD = "YourSecurePassword123!"
.\scripts\Setup-Solution.ps1 -SeedSampleTickets

# Custom environment and location
.\scripts\Setup-Solution.ps1 -Environment "prod" -Location "westus2"
```

### Parameters

- **`-Environment`**: Name of azd environment (default: current environment)
- **`-Location`**: Azure region (default: eastus2)
- **`-SqlAdminPassword`**: SQL Server admin password (prompts if not provided)
- **`-SeedSampleTickets`**: Seeds 5 demo tickets after reset
- **`-SkipDataReset`**: Skips data reset step

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

# Reset and seed 5 sample tickets
.\scripts\Reset-Data.ps1 -SeedSampleTickets

# Only seed, don't reset existing tickets
.\scripts\Reset-Data.ps1 -SeedSampleTickets -SkipReset
```

### Parameters

- **`-ApiBaseUrl`**: API base URL (auto-detects from azd environment if omitted)
- **`-SeedSampleTickets`**: Seeds 5 demo tickets (all New/unassigned)
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

### "SQL password does not meet requirements"
SQL admin password must:
- Be at least 12 characters long
- Contain uppercase and lowercase letters
- Contain numbers
- Contain special characters

---

## CI/CD Usage

For automated pipelines, provide the SQL password via environment variable:

```powershell
$env:SQL_ADMIN_PASSWORD = ${{ secrets.SQL_ADMIN_PASSWORD }}
.\scripts\Setup-Solution.ps1 -SeedSampleTickets
```

This prevents interactive prompts in non-interactive environments.
