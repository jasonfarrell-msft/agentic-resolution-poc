# Setup Guide

## One-Command Setup

Deploy the complete solution with:

```powershell
.\scripts\Setup-Solution.ps1 -SeedSampleTickets
```

This single command provisions all infrastructure, builds containers, configures secrets, and initializes the database in approximately 10–15 minutes.

## Prerequisites

- **Azure CLI** ([install](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)) and authenticated (`az login`)
- **Azure Developer CLI (azd)** ([install](https://aka.ms/install-azd))
- **.NET 8 SDK or later**
- **Azure subscription** with **Contributor** and **User Access Administrator** roles
  - These roles are required to create resources and assign RBAC permissions
  - If unsure, contact your subscription owner or run `az role assignment list --scope /subscriptions/<your-subscription-id>`

## What the Command Does

1. **Validates prerequisites** — Checks Azure CLI, azd, .NET SDK, and authentication status
2. **Prompts for SQL password** — Or reads from `$env:SQL_ADMIN_PASSWORD` if set
3. **Provisions foundation infrastructure** — Azure SQL, Key Vault, and App Service via `azd up`
4. **Builds and pushes containers** — .NET Tickets API and Python Resolution API images to ACR
5. **Creates container apps** — Both APIs deployed with managed identities
6. **Configures secrets and roles** — SQL connection string stored in Key Vault, RBAC permissions assigned
7. **Resets database** — All tickets set to New/unassigned baseline state
8. **Optionally seeds sample data** — 5 demo tickets (with `-SeedSampleTickets` flag)

**Disabled by default:** Admin endpoints are protected during deployment; an ephemeral API key is generated per run and used internally for data reset.

## Usage Examples

### Basic setup with password prompt
```powershell
.\scripts\Setup-Solution.ps1
```

### Setup with sample data (recommended for first-time setup)
```powershell
.\scripts\Setup-Solution.ps1 -SeedSampleTickets
```

### Setup with environment variable password (CI/CD)
```powershell
$env:SQL_ADMIN_PASSWORD = "YourSecurePassword123!"
.\scripts\Setup-Solution.ps1 -SeedSampleTickets
```

### Custom environment and location
```powershell
.\scripts\Setup-Solution.ps1 -Environment "prod" -Location "westus2" -SeedSampleTickets
```

### Foundation resources only (no Container Apps)
```powershell
.\scripts\Setup-Solution.ps1 -SkipContainerApps
```

## Verification

Once the script completes successfully, verify the deployment:

```powershell
# Check Azure resources
az resource list --resource-group "rg-<environment-name>" --query "[].type" -o table

# Test the .NET Tickets API health check
curl -X GET "https://<net-api-url>/api/admin/health"

# Test the Python Resolution API
curl -X GET "https://<python-api-url>/health"

# View tickets
curl -X GET "https://<net-api-url>/api/tickets"
```

API URLs are printed at the end of the setup script and stored in the Web App settings.

## SQL Password Requirements

- Minimum 12 characters
- Must include uppercase, lowercase, numbers, and special characters
- Example format: use a long random password containing uppercase, lowercase, numbers, and special characters.

## Troubleshooting

### Prerequisites Check Fails
- Ensure all three tools are installed: `az version`, `azd version`, `dotnet --version`
- Ensure you're logged in to Azure: `az login`

### Role Assignment Errors
- Verify you have both **Contributor** and **User Access Administrator** roles at the subscription scope
- Run `az role assignment list --assignee <your-email>` to check your role assignments

### Deployment Fails Mid-Process
- Check `azd monitor --logs` for detailed error messages
- Common issues: SQL password complexity, resource naming conflicts, quota limits
- If a resource conflict occurs, use a unique environment name: `.\scripts\Setup-Solution.ps1 -Environment "dev-$(Get-Random)"`

### Container Image Build Fails
- Ensure your .NET project builds locally: `dotnet build src/dotnet/AgenticResolution.sln`
- Check ACR logs in the portal: Resource Group → Container Registry → Build logs

### Cannot Connect to API After Deployment
- Wait 1–2 minutes for the container apps to fully initialize
- Check container app logs in the portal for startup errors
- Verify the API URL from the setup script output or the Web App settings

## Subsequent Deployments

To deploy code changes without reprovisioning:
```bash
azd deploy web
```

To update container images:
```bash
.\scripts\Setup-Solution.ps1 -SkipDataReset
```

To fully reprovision (recreate all resources):
```bash
azd down
.\scripts\Setup-Solution.ps1 -SeedSampleTickets
```

## Security Notes

- SQL connection strings are stored in Azure Key Vault, never in code or plaintext
- Managed identities are used for all Azure service-to-service authentication
- RBAC (role-based access control) is used for Key Vault access
- Admin API key is ephemeral per setup run; production deployments should disable admin endpoints

## See Also

- [Deployment Guide](DEPLOY.md) — Infrastructure details, role requirements, monitoring
- [Architecture](AgenticResolution_Architecture.md) — System design and component overview
