# DB-write guard (PreToolUse on Bash|PowerShell) - Gst API.
# Enforces the repo policy in CLAUDE.md - "Claude never writes to a database" - for BOTH the
# main session and every subagent. Adapted from the MyApp API guard.
#
# Why it matters here: there is no IDesignTimeDbContextFactory, so `dotnet ef` builds ApplicationDbContext
# through Gst.Api's host and uses ConnectionStrings:DefaultConnection - the SHARED SQL Server
# 192.168.120.17 (Gst). A bare `dotnet ef database update` would migrate it for everyone.
#
# Every DB write is DENIED, not merely prompted: a hook `ask` defers to the permission mode and a
# permissive mode then allows it. Only `deny` holds even under bypassPermissions.
# Claude hands over a reviewed .sql plus the exact command; the owner runs it by hand.
# See .claude/agents/database-migration-specialist.md.
#
# Tier 1 : destructive schema ops.
# Tier 2 : any other DB write (apply, inline mutation, script interpreters).
# Everything else falls through silently, including SELECTs, builds, and
# `migrations add|script|list|remove` (those touch files, not a database).
#
# KNOWN COST of matching the whole command string: a command that merely CONTAINS a trigger phrase
# (e.g. a commit message discussing it) is denied too. Reword or use a file; do NOT relax the pattern.
#
# NOTE: only the JSON below may go to stdout; the log write goes to a file.

$ErrorActionPreference = 'SilentlyContinue'

$raw = [Console]::In.ReadToEnd()
try { $payload = $raw | ConvertFrom-Json } catch { exit 0 }

# Bash AND PowerShell - both carry the command on .tool_input.command.
if ($payload.tool_name -notin @('Bash', 'PowerShell')) { exit 0 }

$cmd = $payload.tool_input.command
if ([string]::IsNullOrWhiteSpace($cmd)) { exit 0 }

$who = if ($payload.agent_type) { "agent '$($payload.agent_type)'" } else { 'main session' }

function Write-Denial {
    param([string]$Reason, [string]$Pattern)

    try {
        $log = Join-Path $PSScriptRoot 'db-write-guard.log'
        "$([DateTime]::Now.ToString('s'))  deny  $who  [$Pattern]  $cmd" |
            Out-File -FilePath $log -Append -Encoding utf8
    } catch { }

    @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'deny'
            permissionDecisionReason = $Reason
        }
    } | ConvertTo-Json -Depth 5 -Compress | Write-Output

    exit 0
}

# --- Tier 1: destructive ------------------------------------------------------
foreach ($p in @('database\s+drop', 'DROP\s+DATABASE', 'DROP\s+TABLE')) {
    if ($cmd -imatch $p) {
        Write-Denial "db-write-guard: destructive DB op blocked for $who. Nothing was run. If this is genuinely intended, do it by hand outside Claude." $p
    }
}

# --- Tier 2: any other DB write -----------------------------------------------
$writePatterns = @(
    # Would migrate the shared LAN database configured in appsettings.json.
    '\bdatabase\s+update\b',
    'sqlcmd(?s).*\s-i[\s"'']',
    'sqlcmd(?s).*\b(INSERT|UPDATE|DELETE|ALTER|CREATE|TRUNCATE|MERGE|EXEC|GRANT|DENY|REVOKE)\b',
    # Script indirection: SQL inside a file is invisible to the patterns above, and these
    # interpreters can open a SqlConnection, so they are refused outright. Also in
    # permissions.deny; kept here because a deny rule matches on prefix only.
    '\bdotnet\s+(fsi|script)\b',
    '\b(Invoke-Sqlcmd|Invoke-SqlQuery)\b',
    '\bbcp\b',
    '\bosql\b',
    # Opening a connection by hand from a shell one-liner.
    '(Microsoft\.Data\.SqlClient|System\.Data\.SqlClient)\.SqlConnection'
)

foreach ($p in $writePatterns) {
    if ($cmd -imatch $p) {
        $shown = if ($cmd.Length -gt 400) { $cmd.Substring(0, 400) + ' ...' } else { $cmd }
        Write-Denial @"
db-write-guard: DB writes are owner-run on this repo. Blocked for $who - nothing was run.
Review the SQL, then run this yourself in a terminal:

$shown
"@ $p
    }
}

exit 0
