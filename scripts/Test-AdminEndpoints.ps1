#Requires -Version 7.0
<#
.SYNOPSIS
    Tests the admin endpoints against a local or remote API instance.

.DESCRIPTION
    Runs basic smoke tests against the /api/admin endpoints to verify functionality:
    - Health check (no auth required)
    - Data reset with authentication (optional)
    
    Useful for local development and CI/CD validation.

.PARAMETER ApiBaseUrl
    Base URL of the API to test (default: http://localhost:5000)

.PARAMETER AdminApiKey
    API key for admin endpoint authentication. Required for reset test.

.PARAMETER TestReset
    If specified, also tests the reset-data endpoint (DESTRUCTIVE - resets all tickets)

.EXAMPLE
    .\Test-AdminEndpoints.ps1
    
.EXAMPLE
    .\Test-AdminEndpoints.ps1 -ApiBaseUrl "https://my-api.azurecontainerapps.io" -AdminApiKey "abc123..." -TestReset
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ApiBaseUrl = "http://localhost:5000",
    
    [Parameter(Mandatory = $false)]
    [string]$AdminApiKey,
    
    [Parameter(Mandatory = $false)]
    [switch]$TestReset
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ApiBaseUrl = $ApiBaseUrl.TrimEnd('/')

Write-Host "`n=== Admin Endpoints Test ===" -ForegroundColor Cyan
Write-Host "API URL: $ApiBaseUrl`n" -ForegroundColor Gray

# Test 1: Health Check
Write-Host "[1/2] Testing health endpoint..." -ForegroundColor Yellow
try {
    $healthResponse = Invoke-RestMethod -Uri "$ApiBaseUrl/api/admin/health" -Method Get -TimeoutSec 10
    
    if ($healthResponse.Status -eq "Healthy") {
        Write-Host "  ✓ Health check passed" -ForegroundColor Green
        Write-Host "    Status: $($healthResponse.Status)" -ForegroundColor Gray
        Write-Host "    Database: $($healthResponse.Database)" -ForegroundColor Gray
        Write-Host "    Timestamp: $($healthResponse.Timestamp)" -ForegroundColor Gray
    } else {
        Write-Warning "  Health check returned non-healthy status: $($healthResponse.Status)"
        exit 1
    }
}
catch {
    Write-Error "  ✗ Health check failed: $_"
    exit 1
}

Write-Host ""

# Test 2: Reset Data (optional)
if ($TestReset) {
    Write-Host "[2/2] Testing reset-data endpoint..." -ForegroundColor Yellow
    
    if (-not $AdminApiKey) {
        $AdminApiKey = $env:ADMIN_API_KEY
    }
    
    if (-not $AdminApiKey) {
        Write-Warning "Admin API key required for reset test. Provide -AdminApiKey or set ADMIN_API_KEY environment variable."
        Write-Host "  Skipped - no API key provided" -ForegroundColor Gray
        exit 0
    }
    
    Write-Warning "This will reset all tickets to New/unassigned state!"
    
    $confirm = Read-Host "Continue? (y/N)"
    if ($confirm -ne 'y') {
        Write-Host "  Skipped by user" -ForegroundColor Gray
        exit 0
    }
    
    try {
        $requestBody = @{
            ResetTickets = $true
            SeedSampleTickets = $false
        } | ConvertTo-Json
        
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
        
        Write-Host "  ✓ Reset completed" -ForegroundColor Green
        Write-Host "    Tickets reset: $($resetResponse.TicketsReset)" -ForegroundColor Gray
        Write-Host "    Tickets seeded: $($resetResponse.TicketsSeeded)" -ForegroundColor Gray
        Write-Host "    Message: $($resetResponse.Message)" -ForegroundColor Gray
    }
    catch {
        Write-Error "  ✗ Reset failed: $_"
        if ($_.Exception.Response) {
            $statusCode = $_.Exception.Response.StatusCode.value__
            if ($statusCode -eq 401) {
                Write-Error "Authentication failed. Check your admin API key."
            } elseif ($statusCode -eq 403) {
                Write-Error "Admin endpoints are disabled."
            }
        }
        exit 1
    }
} else {
    Write-Host "[2/2] Skipping reset-data test (use -TestReset to enable)" -ForegroundColor Gray
}

Write-Host "`n✓ All tests passed!" -ForegroundColor Green
