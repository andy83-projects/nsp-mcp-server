[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('win-arm64','win-x86')][string]$Runtime,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$')][string]$Version,
    [string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'out/releases')
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$output=[IO.Path]::GetFullPath($OutputDirectory)
$name="nsp-mcp-server-$Version-$Runtime"
$package=Join-Path $output $name
if(Test-Path -LiteralPath $package){throw "Output already exists: $package. Use a fresh output directory."}
New-Item -ItemType Directory -Path $package -Force | Out-Null
Push-Location $root
try {
    & dotnet publish src/Nsp.Mcp.Server -c Release -r $Runtime --self-contained true `
        -p:EnableWindowsTargeting=true -p:PublishSingleFile=false -p:PublishTrimmed=false `
        "-p:Version=$Version" -o $package
    if($LASTEXITCODE -ne 0){throw "dotnet publish failed for $Runtime"}
    foreach($file in @('Nsp.Mcp.Server.exe','Nsp.Mcp.Server.dll','coreclr.dll','hostfxr.dll','PresentationFramework.dll','Microsoft.AspNetCore.dll')){
        if(-not (Test-Path -LiteralPath (Join-Path $package $file))){throw "Self-contained package is missing $file"}
    }
    Copy-Item -LiteralPath (Join-Path $root 'packaging/Start-NspMcp.cmd') -Destination $package
    Copy-Item -LiteralPath (Join-Path $root 'packaging/QUICKSTART.md') -Destination $package
    foreach($file in @('README.md','SECURITY.md','CHANGELOG.md')){
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $package
    }
    Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $package -Recurse
    Copy-Item -LiteralPath (Join-Path $root 'src/Nsp.TuningTables/clamp-ranges.example.json') -Destination $package
    $commit=(& git rev-parse HEAD).Trim()
    if($LASTEXITCODE -ne 0){throw 'Unable to identify the source commit.'}
    [ordered]@{version=$Version;runtime=$Runtime;commit=$commit;selfContained=$true} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'build-info.json') -Encoding utf8
    $zip=Join-Path $output "$name.zip"
    Compress-Archive -Path $package -DestinationPath $zip -CompressionLevel Optimal
    $hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name.zip" | Set-Content -LiteralPath "$zip.sha256" -Encoding ascii
    Write-Host "Created $zip"
} finally {Pop-Location}
