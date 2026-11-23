<#
.SYNOPSIS
    Removes untracked transient build artifacts from the repository root.
.DESCRIPTION
    Safely cleans loose compiled binaries, logs, and temporary files in the repo root
    without touching source files, Assets/, or backup directories (old/, dist_*, DynamicIsland_Memos/).
    Supports -WhatIf and -Confirm.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param()

$repoRoot = Split-Path -Parent $PSScriptRoot
$patterns = @(
    '*.dll',
    '*.pdb',
    '*.deps.json',
    '*.runtimeconfig.json',
    'DynamicIsland.exe',
    'msbuild.log',
    'test_output.tmp'
)

$removed = 0
foreach ($pattern in $patterns) {
    $items = Get-ChildItem -Path $repoRoot -Filter $pattern -File -ErrorAction SilentlyContinue
    foreach ($item in $items) {
        if ($PSCmdlet.ShouldProcess($item.FullName, 'Remove transient root build artifact')) {
            Remove-Item -LiteralPath $item.FullName -Force
            $removed++
        }
    }
}

Write-Host "CleanWorkspace complete ($removed file(s) removed)."
