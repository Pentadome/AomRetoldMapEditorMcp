param(
    [Parameter(Mandatory = $true)][string]$Archive,
    [string]$NpmPackage
)
$ErrorActionPreference = 'Stop'
$Archive = (Resolve-Path -LiteralPath $Archive).ProviderPath
$expected = ((Get-Content -LiteralPath "$Archive.sha256" -Raw).Trim() -split '\s+')[0]
if ((Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash -ne $expected) { throw 'ZIP checksum mismatch.' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('AomMcp-package-' + [guid]::NewGuid())
try {
    Expand-Archive -LiteralPath $Archive -DestinationPath $root
    foreach ($name in @('AomMcp.exe', 'AomMcp.dll', 'AomMcp.runtimeconfig.json', 'AomEditorBridge.dll',
        'trigger-controller-template.trg', 'README.md', 'TOOLS.md', 'LICENSE', 'setup.ps1',
        'uilayouts/playtest-alt-en-2560x1440.json', 'fixtures/playtest-ui-evidence.json',
        'uilayouts/playtest-normal-en-2560x1440.json', 'fixtures/playtest-ui-evidence-normal-en-2560x1440.json',
        'uilayouts/playtest-alt-en-1920x1080.json', 'fixtures/playtest-ui-evidence-alt-en-1920x1080.json',
        'uilayouts/playtest-normal-en-1920x1080.json', 'fixtures/playtest-ui-evidence-normal-en-1920x1080.json')) {
        if (!(Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf)) { throw "Missing package file: $name" }
    }
    if (!(Get-ChildItem -LiteralPath (Join-Path $root 'layouts') -Filter '*.json')) { throw 'Reviewed layouts missing.' }
    # Shipped XS API must stay signature-only: official syscall help text is read from the user's install, never packaged.
    $xsApi = Join-Path $root 'xs/xs_api.json'
    if (!(Test-Path -LiteralPath $xsApi -PathType Leaf) -or !(Test-Path -LiteralPath (Join-Path $root 'xs/xs_summaries.json') -PathType Leaf)) { throw 'XS API files missing.' }
    if ((Get-Content -LiteralPath $xsApi -Raw) -match '"help"\s*:') { throw 'XS API contains official help text.' }
    $config = Get-Content -LiteralPath (Join-Path $root 'AomMcp.runtimeconfig.json') -Raw | ConvertFrom-Json
    if (!$config.runtimeOptions.includedFrameworks -or $config.runtimeOptions.framework -or $config.runtimeOptions.frameworks) {
        throw 'Package must be self-contained, not require installed .NET runtime.'
    }
    if (Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
        $_.Name -eq 'game_catalog.json' -or $_.Extension -in @('.bar', '.XMB', '.mythscn') -or
        $_.FullName -match '[\\/](generated|crybar|research|bin|obj)[\\/]'
    }) { throw 'Package contains game/generated/research files.' }
    foreach ($name in @('AomMcp.exe', 'AomEditorBridge.dll')) {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $root $name))
        # PE/COFF specification: DOS e_lfanew at 0x3c, Machine after four-byte PE signature; AMD64=0x8664.
        $pe = [BitConverter]::ToInt32($bytes, 0x3c)
        if ([BitConverter]::ToUInt16($bytes, $pe + 4) -ne 0x8664) { throw "$name is not Windows x64." }
    }
    $tokens = $null; $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'setup.ps1'), [ref]$tokens, [ref]$errors)
    if ($errors) { throw ($errors | Out-String) }
    # Deliberately nonexistent executable/UI proves local checks do not need installed game/catalogs.
    & (Join-Path $root 'AomMcp.exe') --exe (Join-Path $root 'missing-game.exe') --ui (Join-Path $root 'missing-ui') --self-test-local
    if ($LASTEXITCODE -ne 0) { throw 'Packaged managed/native self-tests failed.' }
    foreach ($mode in @('core', 'full')) {
        & (Join-Path $root 'AomMcp.exe') --toolset $mode --exe (Join-Path $root 'missing-game.exe') --self-test-local
        if ($LASTEXITCODE -ne 0) { throw "Packaged $mode CLI/self-tests failed." }
    }
    if ($NpmPackage) {
        $NpmPackage = (Resolve-Path -LiteralPath $NpmPackage).ProviderPath
        $checksum = Join-Path (Split-Path $NpmPackage) 'npm-package.sha256'
        $expectedNpm = ((Get-Content -LiteralPath $checksum -Raw).Trim() -split '\s+')[0]
        if ((Get-FileHash -LiteralPath $NpmPackage -Algorithm SHA256).Hash -ne $expectedNpm) {
            throw 'npm package checksum mismatch.'
        }
        $entries = & "$env:SystemRoot\System32\tar.exe" -tf $NpmPackage
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect npm tarball.' }
        if ($entries | Where-Object {
            $_ -match '\.(bar|xmb|mythscn)$|game_catalog\.json$|/(generated|crybar|research|test|scripts|node_modules)/'
        }) { throw 'npm tarball contains game/generated/development files.' }
        $npmRoot = Join-Path $root 'npm-install'
        & npm.cmd install --offline --ignore-scripts --no-audit --no-fund --package-lock=false --prefix $npmRoot $NpmPackage
        if ($LASTEXITCODE -ne 0) { throw 'Offline npm install failed.' }
        $shim = Join-Path $npmRoot 'node_modules\.bin\aom-editor-mcp.cmd'
        $runtime = & $shim --runtime-dir
        if ($LASTEXITCODE -ne 0) { throw 'Installed npm command shim failed.' }
        $npmLicense = Join-Path $npmRoot 'node_modules\aom-retold-editor-mcp\LICENSE'
        if ((Get-FileHash -LiteralPath $npmLicense).Hash -ne
            (Get-FileHash -LiteralPath (Join-Path $root 'LICENSE')).Hash) { throw 'npm MIT license mismatch.' }
        $npmManifest = Get-Content -LiteralPath (Join-Path $npmRoot 'node_modules\aom-retold-editor-mcp\package.json') -Raw | ConvertFrom-Json
        if ($npmManifest.license -ne 'MIT') { throw 'npm license must be MIT.' }
        foreach ($name in @('AomMcp.exe', 'AomMcp.dll', 'AomEditorBridge.dll',
            'AomMcp.deps.json', 'AomMcp.runtimeconfig.json', 'trigger-controller-template.trg', 'setup.ps1',
            'uilayouts/playtest-alt-en-2560x1440.json', 'fixtures/playtest-ui-evidence.json',
        'uilayouts/playtest-normal-en-2560x1440.json', 'fixtures/playtest-ui-evidence-normal-en-2560x1440.json',
        'uilayouts/playtest-alt-en-1920x1080.json', 'fixtures/playtest-ui-evidence-alt-en-1920x1080.json',
        'uilayouts/playtest-normal-en-1920x1080.json', 'fixtures/playtest-ui-evidence-normal-en-1920x1080.json')) {
            if ((Get-FileHash -LiteralPath (Join-Path $runtime $name)).Hash -ne
                (Get-FileHash -LiteralPath (Join-Path $root $name)).Hash) {
                throw "npm/ZIP payload mismatch: $name"
            }
        }
        & $shim --toolset full --exe (Join-Path $root 'missing-game.exe') --ui (Join-Path $root 'missing-ui') --self-test-local
        if ($LASTEXITCODE -ne 0) { throw 'Installed npm launcher self-tests failed.' }
        Push-Location $npmRoot
        try {
            & npx.cmd --offline --no -- aom-editor-mcp --toolset core --exe (Join-Path $root 'missing-game.exe') --self-test-local
            if ($LASTEXITCODE -ne 0) { throw 'Offline npx launcher self-tests failed.' }
        } finally { Pop-Location }
        Write-Host 'PASS: npm checksum, MIT license, clean tarball, offline install/npx, Windows shim, native argument/stdout/exit forwarding.'
    }
    Write-Host 'PASS: ZIP/checksum, self-contained Windows x64 package, default/core/full game-free managed/native self-tests.'
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
