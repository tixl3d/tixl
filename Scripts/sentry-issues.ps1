<#
.SYNOPSIS
  Query the Sentry issue tracker for the TiXL project.

.DESCRIPTION
  Reads SENTRY_AUTH_TOKEN (and optional SENTRY_ORG / SENTRY_PROJECT) from
  the repo-root `.env` file (key=value, lines starting with # ignored) or
  from the current environment if already set.

  Output is always JSON on stdout — consumers (the `sentry-fix` skill,
  ad-hoc shell pipes) parse it. Errors go to stderr with a non-zero exit.

.PARAMETER List
  List unresolved issues for the configured project.

.PARAMETER Issue
  Fetch a single issue plus its latest event (stack trace, tags, context).

.PARAMETER Limit
  Max number of issues to return when using -List. Default 25, server caps at 100.

.PARAMETER Query
  Sentry search query. Default: `is:unresolved`.

.EXAMPLE
  .\Scripts\sentry-issues.ps1 -List
  .\Scripts\sentry-issues.ps1 -List -Limit 50 -Query "is:unresolved environment:production"
  .\Scripts\sentry-issues.ps1 -Issue 7514716102
#>

[CmdletBinding(DefaultParameterSetName = 'List')]
param(
    [Parameter(ParameterSetName = 'List')]
    [switch]$List,

    [Parameter(ParameterSetName = 'Issue', Mandatory = $true)]
    [string]$Issue,

    [Parameter(ParameterSetName = 'List')]
    [int]$Limit = 25,

    [Parameter(ParameterSetName = 'List')]
    [string]$Query = 'is:unresolved'
)

$ErrorActionPreference = 'Stop'

function Load-DotEnv {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return }
    Get-Content -LiteralPath $Path | ForEach-Object {
        $line = $_.Trim()
        if (-not $line -or $line.StartsWith('#')) { return }
        $eq = $line.IndexOf('=')
        if ($eq -lt 1) { return }
        $key = $line.Substring(0, $eq).Trim()
        $val = $line.Substring($eq + 1).Trim().Trim('"').Trim("'")
        if (-not [Environment]::GetEnvironmentVariable($key, 'Process')) {
            [Environment]::SetEnvironmentVariable($key, $val, 'Process')
        }
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Load-DotEnv -Path (Join-Path $repoRoot '.env')

$token   = $env:SENTRY_AUTH_TOKEN
$org     = if ($env:SENTRY_ORG)     { $env:SENTRY_ORG }     else { 'tooll' }
$project = if ($env:SENTRY_PROJECT) { $env:SENTRY_PROJECT } else { 'tooll3' }

if (-not $token) {
    [Console]::Error.WriteLine("SENTRY_AUTH_TOKEN not set. Copy .env.example to .env and fill it in,")
    [Console]::Error.WriteLine("or set the env var before running. Token: https://sentry.io/settings/account/api/auth-tokens/")
    exit 2
}

$headers = @{ Authorization = "Bearer $token" }

function Invoke-Sentry {
    param([string]$Url)
    try {
        return Invoke-RestMethod -Uri $Url -Headers $headers -Method Get
    }
    catch {
        [Console]::Error.WriteLine("Sentry API request failed: $($_.Exception.Message)")
        [Console]::Error.WriteLine("URL: $Url")
        exit 3
    }
}

if ($PSCmdlet.ParameterSetName -eq 'Issue') {
    $issueData = Invoke-Sentry "https://sentry.io/api/0/issues/$Issue/"
    $latestEvent = Invoke-Sentry "https://sentry.io/api/0/issues/$Issue/events/latest/"
    $combined = [pscustomobject]@{
        issue = $issueData
        event = $latestEvent
    }
    $combined | ConvertTo-Json -Depth 25
    return
}

# List mode
$encoded = [System.Uri]::EscapeDataString($Query)
$url = "https://sentry.io/api/0/projects/$org/$project/issues/?query=$encoded&limit=$Limit&sort=date"
$issues = Invoke-Sentry $url

# Slim the payload — full list dumps are huge and most fields are not useful.
$slim = $issues | ForEach-Object {
    [pscustomobject]@{
        id          = $_.id
        shortId     = $_.shortId
        title       = $_.title
        culprit     = $_.culprit
        level       = $_.level
        status      = $_.status
        count       = $_.count
        userCount   = $_.userCount
        firstSeen   = $_.firstSeen
        lastSeen    = $_.lastSeen
        permalink   = $_.permalink
        platform    = $_.platform
        type        = $_.type
        metadata    = $_.metadata
    }
}

$slim | ConvertTo-Json -Depth 10
