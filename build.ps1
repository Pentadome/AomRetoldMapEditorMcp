param([switch]$Generate, [switch]$Test)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & cmd.exe /d /c native\build.cmd
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
    & dotnet build src\AomMcp -c Release
    if ($LASTEXITCODE -ne 0) { throw '.NET build failed' }
    $app = Join-Path $PSScriptRoot 'src\AomMcp\bin\Release\net10.0-windows\AomMcp.dll'
    if ($Generate) {
        & dotnet $app --generate (Join-Path $PSScriptRoot 'generated')
        if ($LASTEXITCODE -ne 0) { throw 'Generation failed' }
    }
    if ($Test) {
        & dotnet $app --self-test
        if ($LASTEXITCODE -ne 0) { throw 'Self-tests failed' }
    }
} finally { Pop-Location }
