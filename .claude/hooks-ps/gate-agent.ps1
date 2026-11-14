# Token-control hook (PreToolUse on Agent|Task) - Gst API.
# Emits an "ask" permission decision so EVERY agent/subagent spawn prompts the user
# for approval - enforcing the "one agent at a time, user in control" rule.
# Also appends a timestamped line to agent-spawn.log for transparency.
# NOTE: only the JSON below may go to stdout; the log write goes to a file.

$ErrorActionPreference = 'SilentlyContinue'
try {
    $log = Join-Path $PSScriptRoot 'agent-spawn.log'
    "$([DateTime]::Now.ToString('s'))  agent spawn gated (ask)" | Out-File -FilePath $log -Append -Encoding utf8
} catch { }

'{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"ask","permissionDecisionReason":"Agent spawn gated by token-control hook - approve one at a time."}}' | Write-Output
