param(
    [Parameter(Mandatory = $true)][string]$GameExe,
    [ValidateSet('core', 'full')][string]$Toolset = 'core'
)
$ErrorActionPreference = 'Stop'
$app = Join-Path $PSScriptRoot 'AomMcp.exe'
$generated = Join-Path $PSScriptRoot 'generated'
if ($env:OS -ne 'Windows_NT') { throw 'Package runs on Windows x64 only.' }
if (!(Test-Path -LiteralPath $app -PathType Leaf)) { throw 'Run setup.ps1 from extracted release folder.' }
$exe = (Resolve-Path -LiteralPath $GameExe).ProviderPath
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$layout = Join-Path $PSScriptRoot "layouts\$hash.json"
if (!(Test-Path -LiteralPath $layout -PathType Leaf)) {
    throw 'Unsupported game build. Obtain independently reviewed layout/release; never copy offsets from another build.'
}
& $app --exe $exe --generate $generated
if ($LASTEXITCODE -ne 0) { throw 'Metadata generation failed.' }
# Generator reports extraction failures without always returning failure; verify both required outputs.
if (!(Test-Path -LiteralPath (Join-Path $generated 'game_catalog.json') -PathType Leaf) -or
    !(Test-Path -LiteralPath (Join-Path $generated 'ui') -PathType Container)) {
    throw 'Game/UI metadata missing. Inspect generation output; setup is incomplete.'
}
Write-Host 'Copy this entry into MCP client configuration; do not overwrite existing entries:'
# JSON nesting: root -> mcpServers -> server -> args; depth 5 preserves nested values.
@{ mcpServers = @{ 'aom-editor' = @{ command = $app; args = @('--exe', $exe, '--toolset', $Toolset) } } } | ConvertTo-Json -Depth 5
