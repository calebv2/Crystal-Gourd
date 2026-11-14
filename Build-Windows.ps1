param(
    [Parameter(Mandatory)]
    [string]$GamePath,
    [string]$CompilerPath = 'C:/Program Files/Unity/Hub/Editor/2020.3.23f1/Editor/Data/Tools/Roslyn/csc.exe',
    [string]$SyncLibPath,
    [string]$BundlePath = (Join-Path $PSScriptRoot 'BuildAssets/crystal-gourd-art')
)

$ErrorActionPreference = 'Stop'
$game = (Resolve-Path -LiteralPath $GamePath).Path
$compiler = (Resolve-Path -LiteralPath $CompilerPath).Path
$bundle = (Resolve-Path -LiteralPath $BundlePath).Path
$managed = Join-Path $game 'A Township Tale_Data/Managed'
$loader = Join-Path $game 'MelonLoader/net35'
if (!$SyncLibPath) { $SyncLibPath = Join-Path $game 'Mods/SyncLib.dll' }

$references = Get-ChildItem -LiteralPath $managed -Filter '*.dll' -File |
    ForEach-Object { '/reference:"' + $_.FullName + '"' }
foreach ($path in @((Join-Path $loader 'MelonLoader.dll'), (Join-Path $loader '0Harmony.dll'), $SyncLibPath)) {
    $references += '/reference:"' + (Resolve-Path -LiteralPath $path).Path + '"'
}

$build = (New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'bin')).FullName
$source = Join-Path $PSScriptRoot 'CrystalGourd.cs'
foreach ($variant in @('Client', 'Server', 'Combined')) {
    $name = if ($variant -eq 'Combined') { 'CrystalGourd' } else { "CrystalGourd.$variant" }
    $output = Join-Path $build "$name.dll"
    $response = Join-Path $build "$name.rsp"
    $arguments = @(
        '/nologo', '/target:library', '/langversion:latest', '/nostdlib+', '/optimize+',
        ('/out:"' + $output + '"'),
        ('/resource:"' + $bundle + '",CrystalGourd.Art'),
        ('"' + $source + '"')
    )
    if ($variant -ne 'Combined') { $arguments += '/define:GOURD_' + $variant.ToUpperInvariant() }
    $arguments + $references | Set-Content -LiteralPath $response -Encoding UTF8
    & $compiler ('@' + $response)
    if ($LASTEXITCODE -ne 0) { throw "Crystal Gourd $variant compilation failed" }
    Write-Output $output
}
