[CmdletBinding()]
param([Parameter(Mandatory)][string]$ZipPath)
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'Package startup checks require Windows (PowerShell 7).'}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('nsp-package-'+[guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $ZipPath -DestinationPath $temp
$directories=@(Get-ChildItem -LiteralPath $temp -Directory)
if($directories.Count -ne 1){throw 'ZIP must contain exactly one package directory.'}
$directory=$directories[0].FullName
$info=Get-Content -LiteralPath (Join-Path $directory 'build-info.json') -Raw | ConvertFrom-Json
$exe=Join-Path $directory 'Nsp.Mcp.Server.exe'
# Validate the actual apphost architecture, rather than trusting the filename.
$bytes=[IO.File]::ReadAllBytes($exe)
$pe=[BitConverter]::ToInt32($bytes,0x3c)
$machine=[BitConverter]::ToUInt16($bytes,$pe+4)
$expected=if($info.runtime -eq 'win-arm64'){0xaa64}else{0x14c}
if($machine -ne $expected){throw "Unexpected PE architecture: $machine for $($info.runtime)"}
$runtime=Get-Content -LiteralPath (Join-Path $directory 'Nsp.Mcp.Server.runtimeconfig.json') -Raw | ConvertFrom-Json
if(-not $runtime.runtimeOptions.includedFrameworks -or $runtime.runtimeOptions.frameworks){throw 'Package is not self-contained.'}
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$listener.Start()
$port=$listener.LocalEndpoint.Port
$listener.Stop()
$stdout=Join-Path $temp 'stdout.log'
$stderr=Join-Path $temp 'stderr.log'
$process=$null
$sessionHeaders=@{Accept='application/json, text/event-stream'}
function Invoke-McpRequest($Payload) {
    $response=Invoke-WebRequest -Uri "http://127.0.0.1:$port/" -Method Post -Headers $sessionHeaders `
        -ContentType 'application/json' -Body ($Payload | ConvertTo-Json -Depth 20 -Compress) -TimeoutSec 15
    if($response.Headers['Mcp-Session-Id']){$sessionHeaders['Mcp-Session-Id']=[string]($response.Headers['Mcp-Session-Id'] | Select-Object -First 1)}
    if([string]::IsNullOrWhiteSpace($response.Content)){return $null}
    $content=[string]$response.Content
    if($response.Headers['Content-Type'] -match 'text/event-stream'){
        $data=@($content -split '\r?\n' | Where-Object {$_ -match '^data: ?'} | ForEach-Object {$_ -replace '^data: ?',''})
        if($data.Count -ne 1){throw "Expected one MCP response, got $content"}
        $content=$data[0]
    }
    $message=$content | ConvertFrom-Json
    if($message.error){throw ($message.error | ConvertTo-Json -Compress)}
    return $message.result
}
try {
    # Launch the apphost directly; there is no dotnet command or installed runtime lookup.
    $process=Start-Process -FilePath $exe -WorkingDirectory $directory -ArgumentList @("--Nsp:Server:Port=$port") `
        -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $deadline=[DateTime]::UtcNow.AddSeconds(60)
    $connected=$false
    while([DateTime]::UtcNow -lt $deadline){
        if($process.HasExited){throw "Server exited: $($process.ExitCode)"}
        try {
            $initialized=Invoke-McpRequest @{jsonrpc='2.0';id=1;method='initialize';params=@{protocolVersion='2025-03-26';capabilities=@{};clientInfo=@{name='release-smoke';version='1.0'}}}
            if(-not $initialized.protocolVersion){throw 'Missing protocol version.'}
            $connected=$true
            break
        } catch {Start-Sleep -Milliseconds 300}
    }
    if(-not $connected){throw 'MCP initialization failed within 60 seconds.'}
    $sessionHeaders['MCP-Protocol-Version']=$initialized.protocolVersion
    $null=Invoke-McpRequest @{jsonrpc='2.0';method='notifications/initialized'}
    $tools=Invoke-McpRequest @{jsonrpc='2.0';id=2;method='tools/list';params=@{}}
    foreach($name in @('nsp_operation_status','nsp_list_tables','nsp_get_app_state')){
        if($name -notin $tools.tools.name){throw "Missing tool: $name"}
    }
    $status=Invoke-McpRequest @{jsonrpc='2.0';id=3;method='tools/call';params=@{name='nsp_operation_status';arguments=@{}}}
    if($status.isError){throw 'Status tool failed.'}
    $state=($status.content | Where-Object type -eq 'text' | Select-Object -First 1).text | ConvertFrom-Json
    if($state.worker.phase -ne 'ready' -or -not $state.worker.workerAlive){throw "Worker failed startup: $($state | ConvertTo-Json -Depth 8 -Compress)"}
    $tables=Invoke-McpRequest @{jsonrpc='2.0';id=4;method='tools/call';params=@{name='nsp_list_tables';arguments=@{}}}
    $catalog=($tables.content | Where-Object type -eq 'text' | Select-Object -First 1).text | ConvertFrom-Json
    if($tables.isError -or @($catalog.tables).Count -lt 16){throw 'Table catalog check failed.'}
    Write-Host "PASS: $($info.runtime) apphost, self-contained runtime, worker, MCP handshake, tools and table catalog."
} catch {
    if(Test-Path $stdout){Get-Content $stdout | Write-Host}
    if(Test-Path $stderr){Get-Content $stderr | Write-Host}
    throw
} finally {
    # Test-owned processes only. The worker's kill-on-close job ends with its parent.
    if($process -and -not $process.HasExited){Stop-Process -Id $process.Id -Force; $process.WaitForExit()}
    if($process){$process.Dispose()}
    Remove-Item -LiteralPath $temp -Recurse -Force
}
