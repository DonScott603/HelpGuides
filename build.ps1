<#
  build.ps1 - Compiles PSR Clone using the .NET Framework C# compiler (csc.exe)
  that ships with every Windows installation. No SDK or runtime install required.

  Usage:
    .\build.ps1            # build
    .\build.ps1 -Run       # build then launch
    .\build.ps1 -SelfTest  # build then run the headless report self-test
#>
param(
    [switch]$Run,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$src  = Join-Path $root 'src'
$outDir = Join-Path $root 'bin'
$exe = Join-Path $outDir 'PsrClone.exe'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Locate the newest .NET Framework 4.x csc.exe
$fwRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64'
if (-not (Test-Path $fwRoot)) { $fwRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework' }
$csc = Get-ChildItem $fwRoot -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'csc.exe') } |
    Sort-Object Name -Descending | Select-Object -First 1 |
    ForEach-Object { Join-Path $_.FullName 'csc.exe' }
if (-not $csc) { throw "csc.exe (.NET Framework) not found under $fwRoot" }
Write-Host "Using compiler: $csc"

# Resolve GAC assemblies for UI Automation / WindowsBase.
$gac = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL'
function Resolve-Gac($name) {
    $f = Get-ChildItem (Join-Path $gac $name) -Recurse -Filter "$name.dll" -ErrorAction SilentlyContinue |
         Select-Object -First 1
    if (-not $f) { throw "GAC assembly not found: $name" }
    return $f.FullName
}
$uiaClient = Resolve-Gac 'UIAutomationClient'
$uiaTypes  = Resolve-Gac 'UIAutomationTypes'
$winBase   = Resolve-Gac 'WindowsBase'

$refs = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Xml.dll',
    'System.IO.Compression.dll',
    'System.IO.Compression.FileSystem.dll',
    $uiaClient,
    $uiaTypes,
    $winBase
)

$sources = Get-ChildItem (Join-Path $src '*.cs') | ForEach-Object { $_.FullName }
$manifest = Join-Path $src 'app.manifest'

$refArgs = $refs | ForEach-Object { "/reference:$_" }

$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    "/out:$exe",
    "/win32manifest:$manifest",
    '/optimize+',
    '/warn:2'
) + $refArgs + $sources

Write-Host "Compiling $($sources.Count) source files..."
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed (csc exit $LASTEXITCODE)" }
Write-Host "Build succeeded -> $exe"

if ($SelfTest) {
    $zip = Join-Path $outDir 'selftest.zip'
    & $exe '--selftest' $zip
    if ($LASTEXITCODE -ne 0) { throw "Self-test failed (exit $LASTEXITCODE)" }
}
if ($Run) { Start-Process $exe }
