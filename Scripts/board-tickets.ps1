<#
.SYNOPSIS
  Read feature tickets from the TiXL road-map GitHub Projects (v2) board.

.DESCRIPTION
  Queries the org-level Projects v2 board ("TiXL road map", org `tixl3d`,
  project number 3) through the GitHub GraphQL API via `gh api graphql`.

  Read-only. This script never writes to GitHub — it does not move cards,
  comment, close, or edit anything. It only lists / fetches tickets so the
  `process-tickets` skill can work them locally.

  Output is always JSON on stdout — consumers (the `process-tickets` skill,
  ad-hoc shell pipes) parse it. Errors go to stderr with a non-zero exit.

  Requires the `read:project` scope on the gh token. Projects v2 data is
  only reachable through GraphQL (no anonymous / REST path), and a classic
  token is refused the `projectV2` field without that scope even though the
  board itself is public. Add it once with:
      gh auth refresh -s read:project

.PARAMETER List
  List every board item whose Status equals -Status (default "In progress")
  as a JSON array. Each record carries the issue number, title, url, repo,
  size, milestone, labels and the full body — enough to work the ticket
  without a second call.

.PARAMETER Ticket
  Fetch a single board item by its issue number (full record, same shape as
  a -List element). Convenience for re-reading one ticket.

.PARAMETER Status
  Status column to filter by. Default "In progress". Valid board values:
  Todo, In progress, Done, Closed.

.PARAMETER Owner
  Org login that owns the board. Default "tixl3d".

.PARAMETER Project
  Projects v2 number. Default 3.

.EXAMPLE
  .\Scripts\board-tickets.ps1 -List
  .\Scripts\board-tickets.ps1 -List -Status Todo
  .\Scripts\board-tickets.ps1 -Ticket 1081
#>

[CmdletBinding(DefaultParameterSetName = 'List')]
param(
    [Parameter(ParameterSetName = 'List')]
    [switch]$List,

    [Parameter(ParameterSetName = 'Ticket', Mandatory = $true)]
    [int]$Ticket,

    [Parameter(ParameterSetName = 'List')]
    [Parameter(ParameterSetName = 'Ticket')]
    [string]$Status = 'In progress',

    [string]$Owner = 'tixl3d',

    [int]$Project = 3
)

$ErrorActionPreference = 'Stop'

function Write-ErrLine([string]$msg) { [Console]::Error.WriteLine($msg) }

# Page through all board items, return the raw node objects whose Status
# matches $Status. Projects v2 GraphQL has no server-side field filter, so we
# page (100/req) and filter client-side. 243 items => ~3 requests.
function Get-MatchingNodes {
    $nodes = @()
    $after = $null
    while ($true) {
        $afterArg = if ($null -eq $after) { '' } else { ", after: `"$after`"" }
        $query = @"
{
  organization(login: "$Owner") {
    projectV2(number: $Project) {
      items(first: 100$afterArg) {
        pageInfo { hasNextPage endCursor }
        nodes {
          status: fieldValueByName(name: "Status") { ... on ProjectV2ItemFieldSingleSelectValue { name } }
          size:   fieldValueByName(name: "Size")   { ... on ProjectV2ItemFieldSingleSelectValue { name } }
          milestone: fieldValueByName(name: "Milestone") { ... on ProjectV2ItemFieldMilestoneValue { milestone { title } } }
          content {
            ... on Issue {
              number title url state
              repository { nameWithOwner }
              labels(first: 15) { nodes { name } }
              body
            }
            ... on DraftIssue { title body }
          }
        }
      }
    }
  }
}
"@
        # Pass the query via a temp file (-F query=@file). Windows PowerShell
        # 5.1 mangles embedded double quotes when handing a string argument to
        # a native exe, which would strip the quotes off our GraphQL literals.
        # Do NOT use 2>&1 here: Windows PowerShell 5.1 wraps a native exe's
        # stderr lines as ErrorRecords and merges them into the output, which
        # corrupts the JSON parse. Capture stdout only; let gh's own stderr
        # (including INSUFFICIENT_SCOPES) flow to the console.
        $tmp = [System.IO.Path]::GetTempFileName()
        try {
            [System.IO.File]::WriteAllText($tmp, $query, (New-Object System.Text.UTF8Encoding($false)))
            $raw = gh api graphql -F query=@$tmp
        }
        finally {
            Remove-Item $tmp -ErrorAction SilentlyContinue
        }
        if ($LASTEXITCODE -ne 0) {
            Write-ErrLine 'GraphQL query failed (gh error above).'
            Write-ErrLine 'If it mentions INSUFFICIENT_SCOPES, run:  gh auth refresh -s read:project'
            exit 1
        }
        $data = $raw | ConvertFrom-Json
        # gh api graphql returns the full envelope: { data: { organization: ... } }
        $page = $data.data.organization.projectV2.items
        foreach ($n in $page.nodes) {
            if ($n.status.name -eq $Status) { $nodes += $n }
        }
        if (-not $page.pageInfo.hasNextPage) { break }
        $after = $page.pageInfo.endCursor
    }
    return $nodes
}

# Project a raw node down to the slim record the skill consumes.
function ConvertTo-Record($n) {
    [PSCustomObject]@{
        number    = $n.content.number
        title     = $n.content.title
        url       = $n.content.url
        repo      = $n.content.repository.nameWithOwner
        state     = $n.content.state
        status    = $n.status.name
        size      = $n.size.name
        milestone = $n.milestone.milestone.title
        labels    = @($n.content.labels.nodes | ForEach-Object { $_.name })
        body      = $n.content.body
    }
}

$matchNodes = Get-MatchingNodes

if ($PSCmdlet.ParameterSetName -eq 'Ticket') {
    $hit = $matchNodes | Where-Object { $_.content.number -eq $Ticket } | Select-Object -First 1
    if ($null -eq $hit) {
        Write-ErrLine "No '$Status' board item found with issue number $Ticket."
        exit 1
    }
    ConvertTo-Record $hit | ConvertTo-Json -Depth 6 -Compress
    exit 0
}

# -List: emit a JSON array (force array even for 0/1 items) as a single line.
# Compact output avoids consumers having to reassemble multi-line pretty JSON
# that PowerShell would otherwise split into a string array across a pipe.
$records = @($matchNodes | ForEach-Object { ConvertTo-Record $_ })
ConvertTo-Json -InputObject @($records) -Depth 6 -Compress
