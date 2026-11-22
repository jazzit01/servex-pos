param(
    [string]$Compiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Compiler -PathType Leaf)) {
    throw 'Install Inno Setup 6 or pass the full ISCC.exe path with -Compiler.'
}
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'ServeX.csproj'
$stage = Join-Path ([IO.Path]::GetTempPath()) ("ServeX-installer-" + [guid]::NewGuid())
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
try {
    dotnet publish $project -c Release -r win-x64 -p:PublishProfile=WindowsX64 -o $stage
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
    & $Compiler "/DPublishDirectory=$stage" "/DAppVersion=$version" (Join-Path $PSScriptRoot 'ServeX.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)." }
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
