# Deployment Guide

**→ For first-time setup, see [Setup Guide](SETUP.md)**

This page covers deployment details, infrastructure breakdown, role requirements, and troubleshooting for operators and infrastructure engineers.

## Quick Reference

| Task | Command |
|------|---------|
| **First-time setup** | `.\scripts\Setup-Solution.ps1` |
| **Update code only** | `azd deploy web` |
| **Full redeploy** | `azd up` |
| **Clean up** | `azd down` |

## Prerequisites

- [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) installed and authenticated (`az login`)
- [Azure Developer CLI (azd)](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/install-azd) installed
- .NET 8 SDK or later
- An Azure user principal with **Contributor** and **User Access Administrator** roles on the subscription (required for role assignments)

## Infrastructure Deployment

The `Setup-Solution.ps1` script provides a complete deployment experience that handles all provisioning, container builds, and configuration. It calls `azd up` internally as part of the process.

**Note:** Running `azd up` alone only deploys foundation resources (SQL, Key Vault, App Service). Use the setup script for complete deployment including Container Apps and APIs.

### What Gets Deployed

1. **Foundation Resources (via azd up)**:
   - Azure SQL Server and Database
   - Azure Key Vault with RBAC permissions
   - App Service Plan and App Service (Blazor frontend)
   - Managed identities and role assignments
   - Build and deploy the Blazor frontend

2. **Container Apps Infrastructure (via Setup-Solution.ps1)**:
   - Container Apps Environment
   - Azure Container Registry (ACR)
   - Build and push .NET API container image
   - Build and push Python Resolution API container image
   - Create .NET API Container App with managed identity
   - Create Python Resolution API Container App with managed identity
   - Configure ACR Pull permissions
   - Configure Key Vault access for API
   - Update Web App with API URLs

3. **Configuration**:
   - Store SQL connection string in Key Vault
   - Configure admin API key for data reset
   - Set environment variables and secrets
   - Enable admin endpoints (disabled by default in production)

4. **Data Baseline**:
   - Run database migrations on first API startup
   - Reset all tickets to New/unassigned state
    - Seed 15 sample tickets covering common IT support scenarios and 8 knowledge base articles

### What Gets Created

| Resource | Purpose |
|----------|---------|
| Azure SQL Server | Hosts the `agenticresolution` database with Entra-only authentication |
| Azure SQL Database | Stores tickets, KB articles, comments |
| Azure Key Vault | Stores SQL connection string (Entra auth) and secrets |
| App Service Plan (B1) | Linux-based hosting for Blazor web app |
| App Service | Blazor frontend with managed identity |
| Container Apps Environment | Hosts Container Apps |
| Container Registry (ACR) | Stores container images (Basic SKU) |
| .NET API Container App | REST API for tickets CRUD operations |
| Python Resolution API Container App | AI-powered ticket resolution service |
| Managed Identities | System/user-assigned identities for secure access |
| Role Assignments | AcrPull, Key Vault Secrets User, etc. |
| SQL Database Users | Managed identity users with appropriate roles (db_owner for API, read/write for Web App) |

### Initial Setup Flow

When you run `Setup-Solution.ps1` for the first time, it will:
- Check that Azure CLI, azd, and .NET SDK are installed and authenticated
- Discover the currently signed-in Azure CLI user to configure as SQL Entra admin
- Call `azd up` to provision foundation resources (SQL with Entra-only auth)
- Provision Container Apps infrastructure and build container images
- Create database users for managed identities with appropriate permissions
- Reset tickets to New/unassigned baseline state
- Seed 15 demo tickets covering common IT support scenarios and 8 knowledge base articles

**SQL Authentication:** The solution uses Entra (Azure AD) authentication exclusively. The currently signed-in Azure CLI user is configured as the SQL Server admin, and managed identities are granted database access. This ensures compliance with Azure security policies like MCAPS `AzureSQL_WithoutAzureADOnlyAuthentication_Deny`.

See [Setup Guide](SETUP.md) for details and examples.


## Subsequent Deployments

To deploy code changes without reprovisioning infrastructure:

```bash
azd deploy web
```

To rebuild and redeploy container images:

```powershell
.\scripts\Setup-Solution.ps1 -SkipDataReset
```

To fully reprovision all resources:

```bash
azd down
.\scripts\Setup-Solution.ps1
```

## Backend API Deployment

The .NET Tickets API and Python Resolution API are deployed as Azure Container Apps. The setup script handles:

- Container Registry (ACR) provisioning
- Container Apps Environment creation
- Building and pushing container images via `az acr build`
- Creating Container Apps with managed identities
- Role assignments and Key Vault access
- Configuration of connection strings and admin endpoints

Container App URLs are printed at the end of setup and configured in the Web App settings automatically.


## Configuration

### App Settings

The setup script automatically configures the following on the Web App:

- `ApiClient__BaseUrl` — .NET Tickets API URL (Container App)
- `ResolutionApi__BaseUrl` — Python Resolution API URL (Container App)
- `KeyVault__Uri` — Azure Key Vault URI
- `ASPNETCORE_ENVIRONMENT` — `Production`

### Key Vault Secrets

- `sql-connection-string` — Azure SQL Database connection string with Entra (Azure AD) authentication

The Web App and Container Apps use managed identities with **Key Vault Secrets User** role for secure access.


## Role Requirements

The user deploying the solution requires:
- **Contributor** role (to create resources)
- **User Access Administrator** role (to assign RBAC permissions to managed identities)

These roles can be assigned at subscription level by a subscription owner:

```bash
az role assignment create --assignee <user@domain.com> --role Contributor --scope /subscriptions/<subscription-id>
az role assignment create --assignee <user@domain.com> --role "User Access Administrator" --scope /subscriptions/<subscription-id>
```

## Monitoring

- **App Service Logs:** Azure Portal → App Service → Log stream
- **Deployment History:** Azure Portal → App Service → Deployment Center
- **Key Vault Audit:** Azure Portal → Key Vault → Monitoring → Audit logs
- **SQL Database Metrics:** Azure Portal → SQL Database → Monitoring

## Troubleshooting

### Build Failures

Ensure the project builds locally:
```bash
dotnet build src/dotnet/AgenticResolution.Web
```

### Deployment Failures

Check deployment logs:
```bash
azd monitor --logs
```

Or view in the Azure Portal: App Service → Deployment Center → Logs

### Role Assignment Errors

If you see "insufficient privileges" errors:
- Ensure your user has both **Contributor** and **User Access Administrator** roles
- Run `az account show` to verify you're authenticated with the correct account

### SQL Connection Failures

If the API cannot connect to SQL:
- Verify the Key Vault secret `sql-connection-string` exists
- Verify the Web App managed identity has Key Vault Secrets User role
- Check SQL Server firewall rules allow Azure services

### Resource Naming Conflicts

If you encounter naming conflicts (e.g., App Service name already taken), use a more unique environment name:
```bash
azd env new <unique-env-name>
azd up
```

## Clean Up

To delete all provisioned Azure resources:

```bash
azd down
```

This will delete the resource group and all resources within it.

## Security Notes

- SQL connection strings are stored in Key Vault, never in app settings as plaintext
- Managed identities are used for all Azure service-to-service authentication
- RBAC (role-based access control) is used for Key Vault access, not access policies
- SQL Server requires TLS 1.2 minimum
- App Service enforces HTTPS-only connections


