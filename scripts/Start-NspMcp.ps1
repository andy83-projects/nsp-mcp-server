[CmdletBinding()]
param(
    [string]$ServerDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'out/server'),
    [string]$BindAddress='127.0.0.1',
    [ValidateRange(1024,65535)][int]$Port=6274,
    [string]$ExpectedOpenFileName=''
)
$ErrorActionPreference='Stop'
if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT){
    throw 'Run the server on Windows, in the same interactive desktop session as NSP.'
}
$directory=(Resolve-Path -LiteralPath $ServerDirectory).Path
$assembly=Join-Path $directory 'Nsp.Mcp.Server.dll'
if(-not (Test-Path -LiteralPath $assembly)){
    throw 'Server not built. Run dotnet publish src/Nsp.Mcp.Server -c Release -o out/server --self-contained false first.'
}
Write-Host 'Experimental NSP automation: use an offline copy and independently verify all edits.'
Write-Host "Endpoint: http://${BindAddress}:${Port}/ (no authentication or TLS)"
Push-Location $directory
try {
    & dotnet $assembly "--Nsp:Server:BindAddress=$BindAddress" "--Nsp:Server:Port=$Port" "--Nsp:ExpectedOpenFileName=$ExpectedOpenFileName"
    if($LASTEXITCODE -ne 0){throw "Server exited with code $LASTEXITCODE"}
} finally {Pop-Location}
