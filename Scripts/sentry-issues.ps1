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

.PARAMETER Resolve
  Mark an issue resolved. Combine with -InNextRelease to get Sentry's
  "resolved in the next release" semantics — Sentry will auto-resolve once
  the next release tag appears, and auto-reopen as a regression if the
  exception comes back in that release or later.

.PARAMETER InNextRelease
  Modifier for -Resolve. Marks the issue resolved-in-next-release rather
  than immediately resolved.

.PARAMETER Archive
  Mark an issue as archived ("ignored" in Sentry's older API). Use for
  "won't fix": driver bugs, third-party library version mismatches,
  user-environment install errors we can't address from code.

.PARAMETER Limit
  Max number of issues to return when using -List. Default 25, server caps at 100.

.PARAMETER Query
  Sentry search query. Default: `is:unresolved`.

.EXAMPLE
  .\Scripts\sentry-issues.ps1 -List
  .\Scripts\sentry-issues.ps1 -List -Limit 50 -Query "is:unresolved environment:production"
  .\Scripts\sentry-issues.ps1 -Issue 7514716102
  .\Scripts\sentry-issues.ps1 -Resolve 7514716102 -InNextRelease
  .\Scripts\sentry-issues.ps1 -Archive 7465802639
#>

[CmdletBinding(DefaultParameterSetName = 'List')]
param(
    [Parameter(ParameterSetName = 'List')]
    [switch]$List,

    [Parameter(ParameterSetName = 'Issue', Mandatory = $true)]
    [string]$Issue,

    [Parameter(ParameterSetName = 'Resolve', Mandatory = $true)]
    [string]$Resolve,

    [Parameter(ParameterSetName = 'Resolve')]
    [switch]$InNextRelease,

    [Parameter(ParameterSetName = 'Archive', Mandatory = $true)]
    [string]$Archive,

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

if ($PSCmdlet.ParameterSetName -eq 'Resolve') {
    $body = if ($InNextRelease) {
        @{ status = 'resolved'; statusDetails = @{ inNextRelease = $true } } | ConvertTo-Json -Compress
    } else {
        @{ status = 'resolved' } | ConvertTo-Json -Compress
    }
    $url = "https://sentry.io/api/0/issues/$Resolve/"
    try {
        $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Put `
            -ContentType 'application/json' -Body $body
        $detail = if ($InNextRelease) { 'in next release' } else { 'immediately' }
        [Console]::Error.WriteLine("Resolved $($response.shortId) ($detail).")
        [pscustomobject]@{
            shortId = $response.shortId
            id = $response.id
            status = $response.status
            statusDetails = $response.statusDetails
        } | ConvertTo-Json -Depth 5
    }
    catch {
        [Console]::Error.WriteLine("Sentry resolve failed: $($_.Exception.Message)")
        [Console]::Error.WriteLine("URL: $url")
        exit 3
    }
    return
}

if ($PSCmdlet.ParameterSetName -eq 'Archive') {
    $body = @{ status = 'ignored' } | ConvertTo-Json -Compress
    $url = "https://sentry.io/api/0/issues/$Archive/"
    try {
        $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Put `
            -ContentType 'application/json' -Body $body
        [Console]::Error.WriteLine("Archived $($response.shortId).")
        [pscustomobject]@{
            shortId = $response.shortId
            id = $response.id
            status = $response.status
        } | ConvertTo-Json -Depth 3
    }
    catch {
        [Console]::Error.WriteLine("Sentry archive failed: $($_.Exception.Message)")
        [Console]::Error.WriteLine("URL: $url")
        exit 3
    }
    return
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
