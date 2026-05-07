#Requires -Version 7.0
<#
.SYNOPSIS
    Configures Azure SQL database users for managed identities with appropriate permissions.

.DESCRIPTION
    Creates or ensures database users for managed identities exist with proper role assignments:
    - API identity: db_owner (required for EF migrations on startup)
    - Web App identity: db_datareader + db_datawriter
    
    Uses an Azure CLI access token with .NET SqlClient via the currently authenticated Azure CLI user.
    The SQL commands are idempotent and safe to run multiple times.

.PARAMETER ServerFqdn
    Fully qualified domain name of the Azure SQL Server (e.g., server.database.windows.net)

.PARAMETER DatabaseName
    Name of the database to configure users in

.PARAMETER ApiIdentityName
    Name of the API managed identity (for db_owner role)

.PARAMETER WebAppIdentityName
    Name of the Web App managed identity (for db_datareader + db_datawriter roles)

.EXAMPLE
    .\Configure-DatabaseUsers.ps1 `
        -ServerFqdn "myserver.database.windows.net" `
        -DatabaseName "mydb" `
        -ApiIdentityName "id-api-prod" `
        -WebAppIdentityName "app-web-prod"

.NOTES
    Requires Azure CLI authentication with admin rights to the SQL server.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ServerFqdn,
    
    [Parameter(Mandatory = $true)]
    [string]$DatabaseName,
    
    [Parameter(Mandatory = $true)]
    [string]$ApiIdentityName,
    
    [Parameter(Mandatory = $true)]
    [string]$WebAppIdentityName
)

$ErrorActionPreference = "Stop"

Write-Host "`nConfiguring SQL database users for managed identities..." -ForegroundColor Cyan
Write-Host "  Server: $ServerFqdn" -ForegroundColor Gray
Write-Host "  Database: $DatabaseName" -ForegroundColor Gray
Write-Host "  API Identity: $ApiIdentityName (db_owner)" -ForegroundColor Gray
Write-Host "  Web App Identity: $WebAppIdentityName (db_datareader, db_datawriter)" -ForegroundColor Gray

# Create idempotent SQL script
# NOTE: The API identity needs elevated permissions (db_owner) because the API runs EF migrations on startup
# In production, consider separating migration and runtime identities with appropriate least-privilege roles
$sqlScript = @"
-- Create user for API managed identity (needs db_owner for EF migrations)
IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = '$ApiIdentityName')
BEGIN
    CREATE USER [$ApiIdentityName] FROM EXTERNAL PROVIDER;
    ALTER ROLE db_owner ADD MEMBER [$ApiIdentityName];
    PRINT 'Created user $ApiIdentityName with db_owner role (required for EF migrations)';
END
ELSE
BEGIN
    -- Ensure role membership even if user exists (idempotent)
    IF NOT EXISTS (
        SELECT 1 FROM sys.database_role_members drm
        JOIN sys.database_principals u ON drm.member_principal_id = u.principal_id
        JOIN sys.database_principals r ON drm.role_principal_id = r.principal_id
        WHERE u.name = '$ApiIdentityName' AND r.name = 'db_owner'
    )
    BEGIN
        ALTER ROLE db_owner ADD MEMBER [$ApiIdentityName];
        PRINT 'Added db_owner role to existing user $ApiIdentityName';
    END
    ELSE
    BEGIN
        PRINT 'User $ApiIdentityName already exists with db_owner role';
    END
END

-- Create user for Web App managed identity (read/write via Key Vault secrets)
IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = '$WebAppIdentityName')
BEGIN
    CREATE USER [$WebAppIdentityName] FROM EXTERNAL PROVIDER;
    ALTER ROLE db_datareader ADD MEMBER [$WebAppIdentityName];
    ALTER ROLE db_datawriter ADD MEMBER [$WebAppIdentityName];
    PRINT 'Created user $WebAppIdentityName with db_datareader and db_datawriter roles';
END
ELSE
BEGIN
    -- Ensure role memberships even if user exists (idempotent)
    IF NOT EXISTS (
        SELECT 1 FROM sys.database_role_members drm
        JOIN sys.database_principals u ON drm.member_principal_id = u.principal_id
        JOIN sys.database_principals r ON drm.role_principal_id = r.principal_id
        WHERE u.name = '$WebAppIdentityName' AND r.name = 'db_datareader'
    )
    BEGIN
        ALTER ROLE db_datareader ADD MEMBER [$WebAppIdentityName];
        PRINT 'Added db_datareader role to existing user $WebAppIdentityName';
    END
    
    IF NOT EXISTS (
        SELECT 1 FROM sys.database_role_members drm
        JOIN sys.database_principals u ON drm.member_principal_id = u.principal_id
        JOIN sys.database_principals r ON drm.role_principal_id = r.principal_id
        WHERE u.name = '$WebAppIdentityName' AND r.name = 'db_datawriter'
    )
    BEGIN
        ALTER ROLE db_datawriter ADD MEMBER [$WebAppIdentityName];
        PRINT 'Added db_datawriter role to existing user $WebAppIdentityName';
    END
    
    IF EXISTS (
        SELECT 1 FROM sys.database_role_members drm
        JOIN sys.database_principals u ON drm.member_principal_id = u.principal_id
        JOIN sys.database_principals r ON drm.role_principal_id = r.principal_id
        WHERE u.name = '$WebAppIdentityName' AND r.name IN ('db_datareader', 'db_datawriter')
        HAVING COUNT(*) = 2
    )
    BEGIN
        PRINT 'User $WebAppIdentityName already exists with db_datareader and db_datawriter roles';
    END
END
"@

# Save SQL script to a temporary file in the project directory
$sqlScriptPath = Join-Path (Get-Location) "configure-db-users-temp.sql"
try {
    $sqlScript | Out-File -FilePath $sqlScriptPath -Encoding UTF8 -Force
    
    Write-Host "  Executing SQL script via .NET SqlClient with Entra authentication..." -ForegroundColor Gray
    
    # Get access token for Azure SQL using Azure CLI
    Write-Verbose "Acquiring access token from Azure CLI..."
    $tokenResponse = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to acquire access token. Ensure you are logged in with 'az login'. Error: $tokenResponse"
    }
    
    # Use .NET SqlClient to execute with access token (handles long tokens properly)
    $connectionString = "Server=$ServerFqdn;Database=$DatabaseName;Encrypt=True;TrustServerCertificate=False;"
    
    # Load System.Data.SqlClient
    Add-Type -AssemblyName "System.Data.SqlClient"
    
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.AccessToken = $tokenResponse
    
    try {
        $connection.Open()
        Write-Verbose "Connected to database successfully"
        
        # Read and execute SQL script
        $sqlCommands = Get-Content $sqlScriptPath -Raw
        
        # Split by GO statements and execute each batch
        $batches = $sqlCommands -split '\r?\nGO\r?\n'
        $outputMessages = @()
        
        foreach ($batch in $batches) {
            $batch = $batch.Trim()
            if ([string]::IsNullOrWhiteSpace($batch)) { continue }
            
            $command = $connection.CreateCommand()
            $command.CommandText = $batch
            $command.CommandTimeout = 30
            
            # Capture PRINT statements
            $infoHandler = {
                param($sender, $e)
                if ($e.Message) {
                    $script:outputMessages += $e.Message
                }
            }
            $connection.add_InfoMessage($infoHandler)
            
            try {
                $null = $command.ExecuteNonQuery()
            }
            catch {
                Write-Error "SQL execution failed: $_"
                throw
            }
            finally {
                $connection.remove_InfoMessage($infoHandler)
                $command.Dispose()
            }
        }
        
        Write-Host "✓ Database users configured successfully" -ForegroundColor Green
        Write-Host "  • ${ApiIdentityName}: db_owner (required for EF migrations)" -ForegroundColor Gray
        Write-Host "  • ${WebAppIdentityName}: db_datareader, db_datawriter" -ForegroundColor Gray
        
        # Show informational output from SQL script
        if ($outputMessages.Count -gt 0) {
            Write-Host "`n  SQL execution details:" -ForegroundColor Gray
            $outputMessages | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        }
    }
    finally {
        if ($connection.State -eq 'Open') {
            $connection.Close()
        }
        $connection.Dispose()
    }
}
catch {
    Write-Error "Failed to configure database users: $_"
    Write-Host "`nTroubleshooting:" -ForegroundColor Yellow
    Write-Host "  1. Ensure you are authenticated with Azure CLI (az login)" -ForegroundColor Gray
    Write-Host "  2. Verify your account has SQL Server admin rights" -ForegroundColor Gray
    Write-Host "  3. Check that the server firewall allows your IP address" -ForegroundColor Gray
    Write-Host "  4. Verify .NET SqlClient is available (built into PowerShell)" -ForegroundColor Gray
    Write-Host "`nSQL script saved at: $sqlScriptPath" -ForegroundColor Yellow
    Write-Host "You can manually execute via Azure Portal Query Editor as the Entra admin." -ForegroundColor Yellow
    throw
}
finally {
    # Clean up temporary SQL script file
    if (Test-Path $sqlScriptPath) {
        Remove-Item $sqlScriptPath -Force -ErrorAction SilentlyContinue
    }
}
