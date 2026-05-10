#Requires -Version 7.0
<#
.SYNOPSIS
    Complete single-command setup for the Agentic Resolution solution.

.DESCRIPTION
    This script performs a complete deployment including:
    - Infrastructure provisioning (Azure SQL with Entra-only auth, Key Vault, App Service, ACR, Container Apps)
    - Container image builds and pushes (.NET API, Python Resolution API)
    - Role assignments (managed identities, Key Vault access, ACR pull)
    - Database user creation for managed identities (appropriate permissions)
    - Secret configuration (SQL connection strings with Entra auth, admin API keys)
    - Database migrations (automatic on first API startup)
    - Data reset (all tickets to New/unassigned)
    - Sample data seeding (15 demo tickets covering common IT support scenarios)

    SQL Authentication:
    - Uses Entra (Azure AD) authentication exclusively - no SQL passwords
    - Currently signed-in Azure CLI user becomes SQL Server Entra admin
    - Managed identities granted database access with appropriate roles:
      * API identity: db_owner (required for EF migrations on startup)
      * Web App identity: db_datareader + db_datawriter

    Prerequisites:
    - Azure CLI authenticated (az login)
    - Azure Developer CLI installed (azd)
    - User principal with Contributor + User Access Administrator roles
    - Docker not required (uses az acr build in the cloud)

.PARAMETER Environment
    Name of the azd environment. If not provided, uses current azd environment.

.PARAMETER Location
    Azure region for deployment. Default: eastus2

.PARAMETER SeedSampleTickets
    Deprecated — sample tickets are now always seeded during setup. Use -SkipDataReset to skip all data operations.

.PARAMETER SkipDataReset
    If specified, skips the data reset step after deployment.

.PARAMETER SkipContainerApps
    If specified, skips Container Apps provisioning (only deploys foundation resources).

.EXAMPLE
    .\Setup-Solution.ps1
    
.EXAMPLE
    .\Setup-Solution.ps1 -Environment "dev" -Location "eastus2" -SeedSampleTickets
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Environment,
    
    [Parameter(Mandatory = $false)]
    [string]$Location = "eastus2",
    
    [Parameter(Mandatory = $false)]
    [switch]$SeedSampleTickets,
    
    [Parameter(Mandatory = $false)]
    [switch]$SkipDataReset,
    
    [Parameter(Mandatory = $false)]
    [switch]$SkipContainerApps
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:PYTHONIOENCODING = 'utf-8'
$env:PYTHONUTF8 = '1'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()

Write-Host "`n╔═══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║       Agentic Resolution - Single-Command Setup              ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════════╝`n" -ForegroundColor Cyan

# Check prerequisites
Write-Host "Checking prerequisites..." -ForegroundColor Yellow

# Check Azure CLI
try {
    $azVersion = az version --query '\"azure-cli\"' -o tsv 2>$null
    if ($azVersion) {
        Write-Host "✓ Azure CLI: $azVersion" -ForegroundColor Green
    } else {
        throw "Azure CLI not found"
    }
}
catch {
    Write-Error "Azure CLI is not installed or not in PATH. Install from: https://aka.ms/installazurecliwindows"
    exit 1
}

# Check Azure CLI authentication
try {
    $account = az account show 2>$null | ConvertFrom-Json
    if ($account) {
        Write-Host "✓ Azure CLI authenticated as: $($account.user.name)" -ForegroundColor Green
    } else {
        throw "Not authenticated"
    }
}
catch {
    Write-Error "Azure CLI not authenticated. Run 'az login' first."
    exit 1
}

# Check Azure Developer CLI
try {
    $azdVersion = azd version 2>$null
    if ($azdVersion) {
        Write-Host "✓ Azure Developer CLI: installed" -ForegroundColor Green
    } else {
        throw "azd not found"
    }
}
catch {
    Write-Error "Azure Developer CLI (azd) is not installed. Install from: https://aka.ms/install-azd"
    exit 1
}

# Check .NET SDK
try {
    $dotnetVersion = dotnet --version 2>$null
    if ($dotnetVersion) {
        Write-Host "✓ .NET SDK: $dotnetVersion" -ForegroundColor Green
    } else {
        throw ".NET SDK not found"
    }
}
catch {
    Write-Error ".NET SDK is not installed. Install from: https://dotnet.microsoft.com/download"
    exit 1
}

Write-Host ""

# Get current Azure user for SQL Entra admin
Write-Host "Discovering current Azure user for SQL Entra admin..." -ForegroundColor Cyan
try {
    $currentUser = az ad signed-in-user show 2>$null | ConvertFrom-Json
    if (-not $currentUser) {
        throw "Could not retrieve signed-in user"
    }

    $entraAdminLogin = $currentUser.userPrincipalName
    if (-not $entraAdminLogin) {
        $entraAdminLogin = $currentUser.displayName
    }
    $entraAdminObjectId = $currentUser.id

    Write-Host "✓ Entra Admin Login: $entraAdminLogin" -ForegroundColor Green
    Write-Host "✓ Entra Admin Object ID: $entraAdminObjectId" -ForegroundColor Green

    # Get tenant ID from account
    $tenantId = $account.tenantId
    Write-Host "✓ Entra Admin Tenant ID: $tenantId" -ForegroundColor Green
}
catch {
    Write-Error "Failed to retrieve current Azure user. Ensure you are logged in with 'az login'."
    exit 1
}

Write-Host ""

# Generate ephemeral admin API key for setup session
$adminApiKey = [System.Guid]::NewGuid().ToString()
$env:ADMIN_API_KEY = $adminApiKey
Write-Host "Generated ephemeral admin API key for this session" -ForegroundColor Gray

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " Step 1: Infrastructure Provisioning & Deployment" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════`n" -ForegroundColor Cyan

$azdArgs = @('up')
if ($Environment) {
    azd env select $Environment 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Creating new environment: $Environment" -ForegroundColor Cyan
        azd env new $Environment
    }
}
if ($Location) {
    $azdArgs += @('--location', $Location)
}

# Persist infrastructure parameters into azd environment config for Bicep parameter resolution
Write-Host "Configuring infrastructure parameters in azd environment..." -ForegroundColor Cyan
$infraParameters = @{
    environmentName = $Environment
    entraAdminLogin = $entraAdminLogin
    entraAdminObjectId = $entraAdminObjectId
    entraAdminTenantId = $tenantId
}

foreach ($parameter in $infraParameters.GetEnumerator()) {
    if ([string]::IsNullOrWhiteSpace($parameter.Value)) {
        continue
    }

    $configKey = "infra.parameters.$($parameter.Key)"
    azd env config set $configKey $parameter.Value
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to configure azd infrastructure parameter '$configKey'."
        exit $LASTEXITCODE
    }
}

Write-Host "Running: azd $($azdArgs -join ' ')" -ForegroundColor Cyan
Write-Host ""

& azd @azdArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Infrastructure deployment failed. Exit code: $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "`n✓ Infrastructure deployment completed" -ForegroundColor Green

# ════════════════════════════════════════════════════════════════════════════════
# Step 2: Provision Container Apps Infrastructure
# ════════════════════════════════════════════════════════════════════════════════

if (-not $SkipContainerApps) {
    Write-Host "`n═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host " Step 2: Container Apps Provisioning" -ForegroundColor Yellow
    Write-Host "═══════════════════════════════════════════════════════════════`n" -ForegroundColor Cyan
    
    # Get azd environment values
    Write-Host "Retrieving environment configuration..." -ForegroundColor Cyan
    $azdEnvOutput = azd env get-values | Out-String
    $envVars = @{}
    foreach ($line in $azdEnvOutput -split "`n") {
        if ($line -match '^([^=]+)=(.*)$') {
            $envVars[$Matches[1].Trim()] = $Matches[2].Trim().Trim('"')
        }
    }
    
    $envName = $envVars['AZURE_ENV_NAME']
    $resourceGroup = "rg-$envName"
    $location = $envVars['AZURE_LOCATION']
    $kvName = $envVars['AZURE_KEY_VAULT_NAME']
    $sqlConnString = $envVars['SQL_CONNECTION_STRING']  # Available from azd outputs
    
    if (-not $envName) {
        Write-Error "Could not determine environment name from azd environment"
        exit 1
    }
    
    Write-Host "Environment: $envName" -ForegroundColor Gray
    Write-Host "Resource Group: $resourceGroup" -ForegroundColor Gray
    Write-Host "Location: $location" -ForegroundColor Gray
    Write-Host ""
    
    # 2.1: Create Container Apps Environment
    $caeEnvName = "cae-$envName"
    Write-Host "Creating Container Apps Environment: $caeEnvName..." -ForegroundColor Cyan
    
    $existingEnv = az containerapp env show --name $caeEnvName --resource-group $resourceGroup 2>$null
    if ($existingEnv) {
        Write-Host "✓ Container Apps Environment already exists" -ForegroundColor Green
    } else {
        az containerapp env create `
            --name $caeEnvName `
            --resource-group $resourceGroup `
            --location $location `
            --only-show-errors
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to create Container Apps Environment"
            exit $LASTEXITCODE
        }
        Write-Host "✓ Container Apps Environment created" -ForegroundColor Green
    }
    
    # 2.2: Create Container Registry
    $acrName = "cr$($envName -replace '-','')"
    Write-Host "`nCreating Container Registry: $acrName..." -ForegroundColor Cyan
    
    $existingAcr = az acr show --name $acrName --resource-group $resourceGroup 2>$null
    if ($existingAcr) {
        Write-Host "✓ Container Registry already exists" -ForegroundColor Green
    } else {
        az acr create `
            --name $acrName `
            --resource-group $resourceGroup `
            --location $location `
            --sku Basic `
            --admin-enabled false `
            --only-show-errors
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to create Container Registry"
            exit $LASTEXITCODE
        }
        Write-Host "✓ Container Registry created" -ForegroundColor Green
    }
    
    $acrLoginServer = "$acrName.azurecr.io"
    
    # 2.3: Build and Push .NET API Container Image
    Write-Host "`nBuilding .NET API container image..." -ForegroundColor Cyan
    Write-Host "  Context: src/dotnet/AgenticResolution.Api" -ForegroundColor Gray
    Write-Host "  Image: $acrLoginServer/api:latest" -ForegroundColor Gray
    
    $apiImageName = "$acrLoginServer/api:latest"
    
    # Change to repo root for az acr build
    Push-Location (Split-Path $PSScriptRoot -Parent)
    try {
        az acr build `
            --registry $acrName `
            --image api:latest `
            --file src/dotnet/AgenticResolution.Api/Dockerfile `
            src/dotnet/AgenticResolution.Api `
            --no-logs `
            --only-show-errors
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to build .NET API container image"
            exit $LASTEXITCODE
        }
        Write-Host "✓ .NET API image built and pushed" -ForegroundColor Green
    } finally {
        Pop-Location
    }
    
    # 2.4: Build and Push Python Resolution API Container Image
    Write-Host "`nBuilding Python Resolution API container image..." -ForegroundColor Cyan
    Write-Host "  Context: src/python" -ForegroundColor Gray
    Write-Host "  Dockerfile: src/python/resolution_api/Dockerfile" -ForegroundColor Gray
    Write-Host "  Image: $acrLoginServer/resolution:latest" -ForegroundColor Gray
    
    $resolutionImageName = "$acrLoginServer/resolution:latest"
    
    Push-Location (Split-Path $PSScriptRoot -Parent)
    try {
        az acr build `
            --registry $acrName `
            --image resolution:latest `
            --file src/python/resolution_api/Dockerfile `
            src/python `
            --no-logs `
            --only-show-errors
        
        if ($LASTEXITCODE -ne 0) {
            $existingResolutionImage = az acr repository show-tags `
                --name $acrName `
                --repository resolution `
                --query "[?@=='latest'] | [0]" `
                -o tsv 2>$null

            if ($existingResolutionImage) {
                Write-Warning "Failed to rebuild Python Resolution API image; continuing with existing resolution:latest image."
            } else {
                Write-Error "Failed to build Python Resolution API container image"
                exit $LASTEXITCODE
            }
        } else {
            Write-Host "✓ Python Resolution API image built and pushed" -ForegroundColor Green
        }
    } finally {
        Pop-Location
    }
    
    # 2.5: Create .NET API Container App with Managed Identity
    Write-Host "`nCreating .NET API Container App..." -ForegroundColor Cyan
    $apiAppName = "ca-api-$envName"
    
    # Create user-assigned managed identity for API
    $apiIdentityName = "id-api-$envName"
    Write-Host "  Creating managed identity: $apiIdentityName" -ForegroundColor Gray
    
    $apiIdentity = az identity create `
        --name $apiIdentityName `
        --resource-group $resourceGroup `
        --location $location `
        --only-show-errors | ConvertFrom-Json
    
    $apiIdentityId = $apiIdentity.id
    $apiIdentityClientId = $apiIdentity.clientId
    $apiIdentityPrincipalId = $apiIdentity.principalId
    
    Write-Host "  Identity Principal ID: $apiIdentityPrincipalId" -ForegroundColor Gray
    
    # Grant ACR Pull to API identity
    Write-Host "  Granting AcrPull role to API identity..." -ForegroundColor Gray
    $acrId = az acr show --name $acrName --resource-group $resourceGroup --query id -o tsv
    
    az role assignment create `
        --assignee $apiIdentityPrincipalId `
        --role AcrPull `
        --scope $acrId `
        --only-show-errors 2>&1 | Out-Null
    
    # Grant Key Vault Secrets User to API identity
    Write-Host "  Granting Key Vault Secrets User role to API identity..." -ForegroundColor Gray
    $kvId = az keyvault show --name $kvName --resource-group $resourceGroup --query id -o tsv
    
    az role assignment create `
        --assignee $apiIdentityPrincipalId `
        --role "Key Vault Secrets User" `
        --scope $kvId `
        --only-show-errors 2>&1 | Out-Null
    
    # Wait a few seconds for role assignments to propagate
    Start-Sleep -Seconds 10
    
    # Get SQL connection string from Key Vault or environment
    if (-not $sqlConnString) {
        Write-Host "  Retrieving SQL connection string from Key Vault..." -ForegroundColor Gray
        # Try to get from Key Vault - may need a delay for permissions
        $maxRetries = 3
        $retryCount = 0
        while ($retryCount -lt $maxRetries) {
            try {
                $sqlConnString = az keyvault secret show `
                    --vault-name $kvName `
                    --name sql-connection-string `
                    --query value -o tsv 2>$null
                if ($sqlConnString) { break }
            } catch {
                # Fallback: construct from components
            }
            $retryCount++
            Start-Sleep -Seconds 5
        }
        
        # If still not available, construct from azd outputs (Entra auth)
        if (-not $sqlConnString) {
            $sqlServerFqdn = $envVars['SQL_SERVER_FQDN']
            $sqlDbName = $envVars['SQL_DATABASE_NAME']
            $sqlConnString = "Server=tcp:$sqlServerFqdn,1433;Initial Catalog=$sqlDbName;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
        }
    }
    
    # Create Container App
    Write-Host "  Creating Container App: $apiAppName" -ForegroundColor Gray
    
    $existingApiApp = az containerapp show --name $apiAppName --resource-group $resourceGroup 2>$null
    if ($existingApiApp) {
        Write-Host "  Updating existing Container App..." -ForegroundColor Gray

        az containerapp secret set `
            --name $apiAppName `
            --resource-group $resourceGroup `
            --secrets "sql-connection-string=$sqlConnString" `
            --only-show-errors

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to update .NET API Container App secrets"
            exit $LASTEXITCODE
        }

        az containerapp update `
            --name $apiAppName `
            --resource-group $resourceGroup `
            --image $apiImageName `
            --set-env-vars "ASPNETCORE_ENVIRONMENT=Production" "ConnectionStrings__Default=secretref:sql-connection-string" "AdminEndpoints__Enabled=true" "AdminEndpoints__ApiKey=$adminApiKey" "AZURE_CLIENT_ID=$apiIdentityClientId" `
            --only-show-errors
    } else {
        az containerapp create `
            --name $apiAppName `
            --resource-group $resourceGroup `
            --environment $caeEnvName `
            --image $apiImageName `
            --target-port 8080 `
            --ingress external `
            --registry-server $acrLoginServer `
            --registry-identity $apiIdentityId `
            --user-assigned $apiIdentityId `
            --cpu 0.5 --memory 1.0Gi `
            --min-replicas 1 --max-replicas 3 `
            --secrets "sql-connection-string=$sqlConnString" `
            --env-vars "ASPNETCORE_ENVIRONMENT=Production" "ConnectionStrings__Default=secretref:sql-connection-string" "AdminEndpoints__Enabled=true" "AdminEndpoints__ApiKey=$adminApiKey" "AZURE_CLIENT_ID=$apiIdentityClientId" `
            --only-show-errors
    }
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create/update .NET API Container App"
        exit $LASTEXITCODE
    }
    
    # Get API URL
    $apiUrl = az containerapp show --name $apiAppName --resource-group $resourceGroup --query properties.configuration.ingress.fqdn -o tsv
    $apiUrl = "https://$apiUrl"
    Write-Host "✓ .NET API Container App created" -ForegroundColor Green
    Write-Host "  URL: $apiUrl" -ForegroundColor Cyan
    
    # 2.6: Create Python Resolution API Container App
    Write-Host "`nCreating Python Resolution API Container App..." -ForegroundColor Cyan
    $resolutionAppName = "ca-res-$envName"
    
    # Create user-assigned managed identity for Resolution API
    $resolutionIdentityName = "id-resolution-$envName"
    Write-Host "  Creating managed identity: $resolutionIdentityName" -ForegroundColor Gray
    
    $resolutionIdentity = az identity create `
        --name $resolutionIdentityName `
        --resource-group $resourceGroup `
        --location $location `
        --only-show-errors | ConvertFrom-Json
    
    $resolutionIdentityId = $resolutionIdentity.id
    $resolutionIdentityClientId = $resolutionIdentity.clientId
    $resolutionIdentityPrincipalId = $resolutionIdentity.principalId
    
    # Grant ACR Pull to Resolution identity
    Write-Host "  Granting AcrPull role to Resolution identity..." -ForegroundColor Gray
    az role assignment create `
        --assignee $resolutionIdentityPrincipalId `
        --role AcrPull `
        --scope $acrId `
        --only-show-errors 2>&1 | Out-Null

    # Grant least-privilege Azure OpenAI inference access to Resolution identity
    Write-Host "  Granting Azure OpenAI inference role to Resolution identity..." -ForegroundColor Gray
    $openAiEndpoint = $env:AZURE_OPENAI_ENDPOINT
    if ([string]::IsNullOrWhiteSpace($openAiEndpoint)) {
        $openAiEndpoint = "https://oai-agentic-res-src-dev.cognitiveservices.azure.com/"
    }

    try {
        $openAiAccountName = ([Uri]$openAiEndpoint).Host.Split('.')[0]
    }
    catch {
        Write-Error "Invalid Azure OpenAI endpoint '$openAiEndpoint'."
        exit 1
    }

    $openAiAccount = az cognitiveservices account list --only-show-errors |
        ConvertFrom-Json |
        Where-Object { $_.name -eq $openAiAccountName } |
        Select-Object -First 1

    if (-not $openAiAccount) {
        Write-Error "Azure OpenAI account '$openAiAccountName' was not found. Cannot grant Resolution API inference access."
        exit 1
    }

    $openAiAccountId = $openAiAccount.id
    $openAiUserRoleId = "5e0bd9bd-7b93-4f28-af87-19fc36ad61bd" # Cognitive Services OpenAI User
    $openAiRoleDefinitionId = "/subscriptions/$($account.id)/providers/Microsoft.Authorization/roleDefinitions/$openAiUserRoleId"
    $existingOpenAiRoleCount = az role assignment list `
        --assignee $resolutionIdentityPrincipalId `
        --scope $openAiAccountId `
        --query "[?roleDefinitionId=='$openAiRoleDefinitionId'] | length(@)" `
        -o tsv `
        --only-show-errors

    if (-not $existingOpenAiRoleCount) {
        $existingOpenAiRoleCount = 0
    }

    if ([int]$existingOpenAiRoleCount -eq 0) {
        az role assignment create `
            --assignee-object-id $resolutionIdentityPrincipalId `
            --assignee-principal-type ServicePrincipal `
            --role $openAiUserRoleId `
            --scope $openAiAccountId `
            --only-show-errors 2>&1 | Out-Null

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to grant Cognitive Services OpenAI User role to Resolution identity."
            exit $LASTEXITCODE
        }
    }

    Start-Sleep -Seconds 5

    # Create Container App
    Write-Host "  Creating Container App: $resolutionAppName" -ForegroundColor Gray
    
    $existingResolutionApp = az containerapp show --name $resolutionAppName --resource-group $resourceGroup 2>$null
    if ($existingResolutionApp) {
        Write-Host "  Updating existing Container App..." -ForegroundColor Gray
        
        az containerapp update `
            --name $resolutionAppName `
            --resource-group $resourceGroup `
            --image $resolutionImageName `
            --set-env-vars "AZURE_CLIENT_ID=$resolutionIdentityClientId" `
            --only-show-errors
    } else {
        az containerapp create `
            --name $resolutionAppName `
            --resource-group $resourceGroup `
            --environment $caeEnvName `
            --image $resolutionImageName `
            --target-port 8000 `
            --ingress external `
            --registry-server $acrLoginServer `
            --registry-identity $resolutionIdentityId `
            --user-assigned $resolutionIdentityId `
            --cpu 0.5 --memory 1.0Gi `
            --min-replicas 1 --max-replicas 3 `
            --env-vars "AZURE_CLIENT_ID=$resolutionIdentityClientId" `
            --only-show-errors
    }
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create/update Python Resolution API Container App"
        exit $LASTEXITCODE
    }
    
    # Get Resolution API URL
    $resolutionUrl = az containerapp show --name $resolutionAppName --resource-group $resourceGroup --query properties.configuration.ingress.fqdn -o tsv
    $resolutionUrl = "https://$resolutionUrl"
    Write-Host "✓ Python Resolution API Container App created" -ForegroundColor Green
    Write-Host "  URL: $resolutionUrl" -ForegroundColor Cyan
    
    # 2.7: Update Web App to use new Container App URLs
    Write-Host "`nUpdating Web App configuration..." -ForegroundColor Cyan
    $webAppName = $envVars['WEB_APP_NAME']
    
    az webapp config appsettings set `
        --name $webAppName `
        --resource-group $resourceGroup `
        --settings "ApiClient__BaseUrl=$apiUrl" "ApiBaseUrl=$apiUrl" "ResolutionApi__BaseUrl=$resolutionUrl" `
        --only-show-errors | Out-Null

    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Web App configuration updated with new API URLs" -ForegroundColor Green
    } else {
        Write-Warning "Failed to update Web App configuration. You may need to update manually."
    }

    # 2.8: Configure SQL Database Users for Managed Identities
    # Get SQL server details
    $sqlServerFqdn = $envVars['SQL_SERVER_FQDN']
    $sqlDbName = $envVars['SQL_DATABASE_NAME']

    # Call dedicated script to configure database users
    $configureDbScript = Join-Path $PSScriptRoot "Configure-DatabaseUsers.ps1"
    
    try {
        & $configureDbScript `
            -ServerFqdn $sqlServerFqdn `
            -DatabaseName $sqlDbName `
            -ApiIdentityName $apiIdentityName `
            -WebAppIdentityName $webAppName
    }
    catch {
        Write-Warning "Failed to configure database users: $_"
        Write-Host "You may need to manually configure database users via Azure Portal Query Editor." -ForegroundColor Yellow
        Write-Host "Required users:" -ForegroundColor Yellow
        Write-Host "  • $apiIdentityName : db_owner" -ForegroundColor Gray
        Write-Host "  • $webAppName : db_datareader, db_datawriter" -ForegroundColor Gray
    }

} else {
    Write-Host "`nSkipping Container Apps provisioning (SkipContainerApps specified)" -ForegroundColor Gray
}

# Get API URL from azd outputs
Write-Host "`n═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " Step 3: Data Reset & Baseline Configuration" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════`n" -ForegroundColor Cyan

if (-not $SkipDataReset) {
    # Determine API URL - use the newly created one if available
    if ($apiUrl) {
        Write-Host "Using newly deployed API URL: $apiUrl" -ForegroundColor Cyan
    } else {
        # Try to get from azd environment
        $azdEnv = azd env get-values | Out-String
        if ($azdEnv -match 'API_BASE_URL=(.+)') {
            $apiUrl = $Matches[1].Trim().Trim('"')
            Write-Host "Using API URL from azd environment: $apiUrl" -ForegroundColor Cyan
        }
        elseif ($azdEnv -match 'TICKETS_API_URL=(.+)') {
            $apiUrl = $Matches[1].Trim().Trim('"')
            Write-Host "Using API URL from azd environment: $apiUrl" -ForegroundColor Cyan
        }
    }
    
    if (-not $apiUrl) {
        Write-Error "API URL could not be determined. Container Apps may not have been deployed successfully."
        Write-Host "Please verify Container Apps are running with: az containerapp list --resource-group rg-$envName" -ForegroundColor Yellow
        exit 1
    }
    
    Write-Host "Waiting for API to become available (may take 30-60 seconds for first startup)..." -ForegroundColor Yellow
    $maxWaitSeconds = 120
    $waitInterval = 10
    $elapsed = 0
    $apiReady = $false
    
    while ($elapsed -lt $maxWaitSeconds) {
        try {
            $healthCheck = Invoke-RestMethod -Uri "$apiUrl/api/admin/health" -Method Get -TimeoutSec 5 -ErrorAction Stop
            if ($healthCheck) {
                $apiReady = $true
                Write-Host "✓ API is ready" -ForegroundColor Green
                break
            }
        } catch {
            Write-Host "  Waiting... ($elapsed/$maxWaitSeconds seconds)" -ForegroundColor Gray
            Start-Sleep -Seconds $waitInterval
            $elapsed += $waitInterval
        }
    }
    
    if (-not $apiReady) {
        Write-Error "API did not become available within $maxWaitSeconds seconds. Cannot seed sample tickets."
        Write-Host "Try running data reset manually once the API is available:" -ForegroundColor Yellow
        Write-Host "  .\scripts\Reset-Data.ps1 -ApiBaseUrl $apiUrl -AdminApiKey $adminApiKey -SeedSampleTickets" -ForegroundColor Gray
        exit 1
    } else {
        Write-Host "`nResetting data via API..." -ForegroundColor Cyan
        Write-Host "Using admin API key: $($adminApiKey.Substring(0,8))..." -ForegroundColor Gray
        
        $resetArgs = @(
            '-ApiBaseUrl', $apiUrl
            '-AdminApiKey', $adminApiKey
            '-SeedSampleTickets'
        )
        
        $scriptPath = Join-Path $PSScriptRoot "Reset-Data.ps1"
        if (Test-Path $scriptPath) {
            & $scriptPath @resetArgs
        } else {
            Write-Warning "Reset-Data.ps1 script not found. Skipping data reset."
            Write-Host "To reset data manually, run: .\scripts\Reset-Data.ps1 -ApiBaseUrl $apiUrl -AdminApiKey <your-key>" -ForegroundColor Gray
        }
    }
} else {
    Write-Host "Skipping data reset (SkipDataReset specified)" -ForegroundColor Gray
}

Write-Host "`n╔═══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║                  Setup Complete!                              ║" -ForegroundColor Green
Write-Host "╚═══════════════════════════════════════════════════════════════╝`n" -ForegroundColor Green

# Display environment outputs
Write-Host "Environment Details:" -ForegroundColor Cyan
Write-Host "  Environment: $envName" -ForegroundColor Gray
Write-Host "  Resource Group: $resourceGroup" -ForegroundColor Gray
Write-Host "  Location: $location" -ForegroundColor Gray

if (-not $SkipContainerApps -and $apiUrl) {
    Write-Host "`nDeployed Services:" -ForegroundColor Cyan
    Write-Host "  .NET API: $apiUrl" -ForegroundColor Cyan
    Write-Host "  Python Resolution API: $resolutionUrl" -ForegroundColor Cyan
}

Write-Host "`nNext Steps:" -ForegroundColor Yellow
Write-Host "  • View web app: " -NoNewline -ForegroundColor Gray
$webHost = azd env get-values | Select-String "WEB_APP_HOSTNAME" | ForEach-Object { $_ -replace 'WEB_APP_HOSTNAME=', '' } | Select-Object -First 1
if ($webHost) {
    Write-Host "https://$($webHost.Trim().Trim('"'))" -ForegroundColor Cyan
}

if (-not $SkipContainerApps) {
    Write-Host "  • Test API health: curl $apiUrl/api/admin/health" -ForegroundColor Gray
    Write-Host "  • View Container App logs: az containerapp logs show --name ca-api-$envName --resource-group $resourceGroup --follow" -ForegroundColor Gray
}

Write-Host "  • Monitor logs: azd monitor --logs" -ForegroundColor Gray
Write-Host "  • Reset data: .\scripts\Reset-Data.ps1 -ApiBaseUrl $apiUrl -AdminApiKey <key> -SeedSampleTickets" -ForegroundColor Gray
Write-Host "  • Clean up: azd down`n" -ForegroundColor Gray

Write-Host "NOTE: Admin API key for this session: $($adminApiKey.Substring(0,8))... (stored in API environment)" -ForegroundColor Yellow
Write-Host "      Regenerate for production use and store securely.`n" -ForegroundColor Yellow
