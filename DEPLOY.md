# Deployment Guide

## Prerequisites

- [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) installed and authenticated (`az login`)
- [Azure Developer CLI (azd)](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/install-azd) installed
- .NET 8 SDK or later

## First-Time Deployment

1. **Initialize the environment:**
   ```bash
   azd init
   ```
   - Accept the default environment name or provide your own (e.g., `dev`, `staging`, `prod`)
   - Select your Azure subscription
   - Select your preferred region (default: `eastus2`)

2. **Provision and deploy:**
   ```bash
   azd up
   ```
   This command will:
   - Provision the Azure resources (App Service Plan, App Service)
   - Build the Blazor frontend (`AgenticResolution.Web`)
   - Deploy the frontend to Azure App Service

3. **Access your application:**
   After deployment completes, azd will output the web app URL:
   ```
   WEB_APP_HOSTNAME: app-<env>-web.azurewebsites.net
   ```
   Navigate to `https://app-<env>-web.azurewebsites.net` to view the deployed application.

## Subsequent Deployments

To deploy code changes without re-provisioning infrastructure:

```bash
azd deploy web
```

To re-provision infrastructure and deploy:

```bash
azd up
```

## Backend Deployment (Future)

The backend API (`AgenticResolution.Api`) is declared in `azure.yaml` but not yet deployed. When ready to deploy backend services:

1. Edit `infra/resources.bicep` and set `deployBackend` parameter to `true`
2. Complete the backend resource configurations (Container Registry, Container App Environment, etc.)
3. Run `azd up` to provision backend resources and deploy

## Configuration

### App Settings

The following app settings are configured on the App Service:

- `ApiClient__BaseUrl`: Empty by default. Set this to the backend API URL once the Container App API is deployed.
- `ASPNETCORE_ENVIRONMENT`: Set to `Production`

To update app settings:

```bash
az webapp config appsettings set --name app-<env>-web --resource-group rg-<env> --settings ApiClient__BaseUrl=https://your-api-url.azurecontainerapps.io
```

Or update directly in the Azure Portal: App Service → Configuration → Application settings

## Monitoring

- **App Service Logs:** Azure Portal → App Service → Log stream
- **Deployment History:** Azure Portal → App Service → Deployment Center

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
