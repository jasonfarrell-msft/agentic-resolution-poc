#Requires -Version 7.0
<#
.SYNOPSIS
    Resets ticket data to baseline state (New/unassigned) via the API admin endpoint.

.DESCRIPTION
    This script calls the /api/admin/reset-data endpoint to reset all tickets to New status
    and unassigned state. Optionally seeds sample tickets for demo purposes.
    
    Requires authentication via X-Admin-Api-Key header. Admin endpoints must be enabled
    in the API configuration.
    
    Designed to be called after infrastructure deployment to ensure a clean baseline.

.PARAMETER ApiBaseUrl
    Base URL of the Tickets API. If not provided, attempts to read from azd environment outputs.

.PARAMETER AdminApiKey
    API key for admin endpoint authentication. Required. Can be provided via:
    - Parameter
    - Environment variable ADMIN_API_KEY
    - Prompt (if interactive)

.PARAMETER SeedSampleTickets
    If specified, seeds 5 sample tickets (all New/unassigned) after reset.

.PARAMETER SkipReset
    If specified, skips resetting existing tickets and only seeds if requested.

.EXAMPLE
    .\Reset-Data.ps1 -ApiBaseUrl "https://my-api.azurecontainerapps.io" -AdminApiKey "abc123..."
    
.EXAMPLE
    $env:ADMIN_API_KEY = "abc123..."
    .\Reset-Data.ps1 -SeedSampleTickets
    
.EXAMPLE
    .\Reset-Data.ps1 -ApiBaseUrl "http://localhost:5000" -AdminApiKey "local-key" -SeedSampleTickets
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ApiBaseUrl,
    
    [Parameter(Mandatory = $false)]
    [string]$AdminApiKey,
    
    [Parameter(Mandatory = $false)]
    [switch]$SeedSampleTickets,
    
    [Parameter(Mandatory = $false)]
    [switch]$SkipReset
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Helper function to get API URL from azd environment
function Get-AzdApiUrl {
    try {
        Write-Host "Attempting to get API URL from azd environment..." -ForegroundColor Cyan
        
        # Try to get from azd env
        $azdEnv = azd env get-values 2>$null | Out-String
        if ($azdEnv) {
            $lines = $azdEnv -split "`n"
            foreach ($line in $lines) {
                if ($line -match '^API_BASE_URL=(.+)$') {
                    return $Matches[1].Trim('"')
                }
                if ($line -match '^TICKETS_API_URL=(.+)$') {
                    return $Matches[1].Trim('"')
                }
            }
        }
        
        return $null
    }
    catch {
        Write-Verbose "Could not read azd environment: $_"
        return $null
    }
}

# Determine API base URL
if (-not $ApiBaseUrl) {
    $ApiBaseUrl = Get-AzdApiUrl
}

if (-not $ApiBaseUrl) {
    $ApiBaseUrl = $env:API_BASE_URL
}

if (-not $ApiBaseUrl) {
    Write-Error "API base URL not provided and could not be determined from environment. Please specify -ApiBaseUrl parameter."
    exit 1
}

# Determine admin API key
if (-not $AdminApiKey) {
    $AdminApiKey = $env:ADMIN_API_KEY
}

if (-not $AdminApiKey) {
    if ([Environment]::UserInteractive) {
        Write-Host "Admin API key required for authentication." -ForegroundColor Yellow
        $AdminApiKey = Read-Host "Enter admin API key" -MaskInput
    } else {
        Write-Error "Admin API key not provided. Specify -AdminApiKey parameter or set ADMIN_API_KEY environment variable."
        exit 1
    }
}

if ([string]::IsNullOrWhiteSpace($AdminApiKey)) {
    Write-Error "Admin API key cannot be empty."
    exit 1
}

# Ensure URL doesn't end with slash
$ApiBaseUrl = $ApiBaseUrl.TrimEnd('/')

Write-Host "`n=== Ticket Data Reset ===" -ForegroundColor Green
Write-Host "API URL: $ApiBaseUrl" -ForegroundColor Cyan
Write-Host ""

# Check API health first
Write-Host "Checking API health..." -ForegroundColor Yellow
try {
    $healthResponse = Invoke-RestMethod -Uri "$ApiBaseUrl/api/admin/health" -Method Get -TimeoutSec 10
    Write-Host "✓ API is healthy" -ForegroundColor Green
    Write-Host "  Database: $($healthResponse.Database)" -ForegroundColor Gray
}
catch {
    Write-Error "API health check failed. Please ensure the API is running and accessible at $ApiBaseUrl"
    Write-Error "Error: $_"
    exit 1
}

# Build request body
$requestBody = @{
    ResetTickets = -not $SkipReset.IsPresent
    SeedSampleTickets = $SeedSampleTickets.IsPresent
} | ConvertTo-Json

Write-Host ""
if (-not $SkipReset) {
    Write-Host "Resetting all tickets to New/unassigned state..." -ForegroundColor Yellow
}
if ($SeedSampleTickets) {
    Write-Host "Seeding sample tickets..." -ForegroundColor Yellow
}

# Call reset endpoint
try {
    $headers = @{
        "X-Admin-Api-Key" = $AdminApiKey
    }
    
    $resetResponse = Invoke-RestMethod `
        -Uri "$ApiBaseUrl/api/admin/reset-data" `
        -Method Post `
        -Headers $headers `
        -Body $requestBody `
        -ContentType "application/json" `
        -TimeoutSec 30
    
    Write-Host ""
    Write-Host "✓ Reset completed successfully" -ForegroundColor Green
    Write-Host "  Tickets reset: $($resetResponse.TicketsReset)" -ForegroundColor Gray
    Write-Host "  Tickets seeded: $($resetResponse.TicketsSeeded)" -ForegroundColor Gray
    Write-Host "  Message: $($resetResponse.Message)" -ForegroundColor Gray
    Write-Host ""
}
catch {
    Write-Error "Data reset failed."
    Write-Error "Error: $_"
    if ($_.Exception.Response) {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -eq 401) {
            Write-Error "Authentication failed. Check your admin API key."
        } elseif ($statusCode -eq 403) {
            Write-Error "Admin endpoints are disabled. Set AdminEndpoints:Enabled=true in API configuration."
        }
        
        try {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $responseBody = $reader.ReadToEnd()
            Write-Error "Response: $responseBody"
        } catch {
            # Ignore read errors
        }
    }
    exit 1
}

Write-Host "Data reset complete!" -ForegroundColor Green
