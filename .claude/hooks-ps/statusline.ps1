# Team statusline: [model] branch dir - with a [!] drift marker when the
# session model is not the opusplan pair (Opus in plan mode / Sonnet execution).
$ErrorActionPreference = 'SilentlyContinue'
try {
    $data = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $model = $data.model.display_name
    $dir = $data.workspace.current_dir
    if (-not $dir) { $dir = (Get-Location).Path }
    $leaf = Split-Path $dir -Leaf
    $branch = git -C $dir rev-parse --abbrev-ref HEAD 2>$null
    $marker = ''
    if ($model -notmatch 'Opus|Sonnet') { $marker = '[!] ' }
    $line = "$marker[$model]"
    if ($branch) { $line += " $branch" }
    $line += " $leaf"
    Write-Output $line
} catch {
    Write-Output '[statusline error]'
}
