[CmdletBinding()]
param(
    [ValidateSet("all", "fastchess", "bullet")]
    [string]$Tool = "all",
    [switch]$InstallRust,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$toolRoot = Join-Path $repoRoot "artifacts/local-tools"
New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null

if ($InstallRust) {
    $rustupCommand = Get-Command rustup -ErrorAction SilentlyContinue
    $rustupPath = Join-Path $HOME ".cargo/bin/rustup.exe"
    if ($null -eq $rustupCommand -and -not (Test-Path -LiteralPath $rustupPath)) {
        if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
            throw "winget is required to install Rustup. Install Rust manually or rerun on Windows with winget."
        }
        & winget install --id Rustlang.Rustup --exact --accept-source-agreements --accept-package-agreements
        if ($LASTEXITCODE -ne 0) { throw "winget failed to install Rustup (exit $LASTEXITCODE)." }
        Write-Warning "Rustup was installed. Open a new PowerShell session and rerun with -InstallRust to install the stable toolchain."
    }
    else {
        $rustupExecutable = if ($null -ne $rustupCommand) { $rustupCommand.Source } else { $rustupPath }
        & $rustupExecutable toolchain install stable
        if ($LASTEXITCODE -ne 0) { throw "rustup failed to install the stable toolchain (exit $LASTEXITCODE)." }
    }
}

function Save-ToolArchive([string]$Name, [string]$Uri, [string]$Destination, [string]$Marker, [string]$Sha256 = "") {
    $replace = Test-Path -LiteralPath $Destination
    if ($replace -and -not $Force) {
        Write-Host "$Name already exists at $Destination; use -Force to replace it."
        return
    }

    $temporary = Join-Path ([System.IO.Path]::GetTempPath()) "zwischenzug-$Name-$([guid]::NewGuid().ToString('N'))"
    $archive = Join-Path $temporary "source.zip"
    $extract = Join-Path $temporary "extract"
    try {
        New-Item -ItemType Directory -Path $extract -Force | Out-Null
        Invoke-WebRequest -Uri $Uri -OutFile $archive
        if ($Sha256) {
            $actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
            if ($actualHash -ne $Sha256) { throw "$Name archive SHA-256 mismatch: expected $Sha256, received $actualHash." }
        }
        Expand-Archive -LiteralPath $archive -DestinationPath $extract
        $source = Get-ChildItem -LiteralPath $extract -Directory | Where-Object {
            Test-Path -LiteralPath (Join-Path $_.FullName $Marker)
        } | Select-Object -First 1
        if ($null -eq $source) { throw "$Name archive did not contain the expected '$Marker' file." }
        if ($replace) { Remove-Item -LiteralPath $Destination -Recurse -Force }
        Move-Item -LiteralPath $source.FullName -Destination $Destination
        Write-Host "$Name downloaded to $Destination"
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    }
}


if ($Tool -in @("all", "fastchess")) {
    Save-ToolArchive `
        -Name "fastchess" `
        -Uri "https://github.com/Disservin/fastchess/releases/download/v1.8.2-alpha/fastchess-windows-x86-64.zip" `
        -Destination (Join-Path $toolRoot "fastchess-windows-x86-64") `
        -Marker "fastchess.exe" `
        -Sha256 "007ce550dc809510b34622a1aeb43fd443d40284045bef5c1bc39c8941114143"
}

if ($Tool -in @("all", "bullet")) {
    Save-ToolArchive `
        -Name "bullet" `
        -Uri "https://codeload.github.com/jw1912/bullet/zip/869e30934402d912609d3ee2baa9696a12e1f783" `
        -Destination (Join-Path $toolRoot "bullet") `
        -Marker "Cargo.toml"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Warning ".NET 10 SDK is required to build/start Zwischenzug; install it separately. This script does not install the SDK."
}
if (-not (Get-Command cargo -ErrorAction SilentlyContinue) -and -not (Test-Path -LiteralPath (Join-Path $HOME ".cargo/bin/cargo.exe"))) {
    Write-Warning "Rust/Cargo is required to build Bullet. Run this script with -InstallRust or install Rustup separately."
}
Write-Host "CUDA, Visual Studio C++ Build Tools, training datasets, tablebases, and opening books are not downloaded by this script."
