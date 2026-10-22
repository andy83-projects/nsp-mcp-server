param(
 [Parameter(Mandatory=$true)][string]$OriginalMap,
 [string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),
 [string]$ServerDirectory=''
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path $RepositoryRoot).Path
if(-not $ServerDirectory){$ServerDirectory=Join-Path $root 'out\server'}
New-Item (Join-Path $root 'logs') -ItemType Directory -Force | Out-Null
Set-Location $root
$report=[ordered]@{success=$false;startedUtc=[DateTime]::UtcNow.ToString('o')}
$original=(Resolve-Path $OriginalMap).Path
$journal=Join-Path $root 'logs\worker-recovery-integration.json'
$supervisor=$null
function Call-Tool($name,$arguments=@{}) {
 $body=@{jsonrpc='2.0';id=11;method='tools/call';params=@{name=$name;arguments=$arguments}} | ConvertTo-Json -Depth 20 -Compress
 $response=Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:6275/' -Method Post -ContentType 'application/json' -Headers @{Accept='application/json, text/event-stream'} -Body $body -TimeoutSec 35
 $line=@($response.Content -split "`n" | Where-Object {$_ -like 'data:*'})[0]
 $packet=$line.Substring(5).Trim() | ConvertFrom-Json
 return ($packet.result.content[0].text | ConvertFrom-Json)
}
function Assert($condition,$message){if(-not $condition){throw $message}}
function Stop-Test {
 if($null -ne $script:supervisor){if(-not $script:supervisor.HasExited){Stop-Process -Id $script:supervisor.Id -Force};$script:supervisor.WaitForExit(10000)|Out-Null;$script:supervisor=$null}
}
function Start-Test([bool]$frozen=$false) {
 $serverArgs=@((Join-Path $ServerDirectory 'Nsp.Mcp.Server.dll'),'--environment=RecoveryTest','--Nsp:Server:BindAddress=127.0.0.1','--Nsp:Server:Port=6275','--Nsp:WorkerTimeoutSeconds=2',('--Nsp:WorkerJournalPath='+$journal))
 if($frozen){$serverArgs+='--Nsp:RecoveryTestFrozenProbe=true'}else{$serverArgs+='--Nsp:RecoveryTestHangTool=nsp_get_app_state'}
 $script:supervisor=Start-Process dotnet.exe -ArgumentList $serverArgs -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput "$root\recovery-integration-output.log" -RedirectStandardError "$root\recovery-integration-error.log" -PassThru
 $deadline=[DateTime]::UtcNow.AddSeconds(30)
 do{try{$status=Call-Tool 'nsp_operation_status';if($status.worker.workerAlive){return $status}}catch{};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $deadline)
 throw 'Test supervisor did not become ready'
}
try {
 $report.originalHashBefore=(Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash
 $report.nspPidsBefore=@(Get-Process NSP | ForEach-Object {$_.Id})
 if(Test-Path $journal){Remove-Item -LiteralPath $journal}
 $initial=Start-Test
 $report.initial=$initial
 $timeout=Call-Tool 'nsp_get_app_state'
 Assert ($timeout.errorCode -eq 'worker_timeout') 'Injected blocked worker did not time out'
 $report.timeout=$timeout
 $blocked=Call-Tool 'nsp_send_keyboard_shortcut' @{keys='{F4}';confirm=$true}
 Assert ($blocked.errorCode -eq 'recovery_required') 'Quarantine failed to reject UI input'
 $report.blockedInput=$blocked
 $beforeRecovery=Call-Tool 'nsp_operation_status'
 $recovered=Call-Tool 'nsp_recover_worker' @{confirm=$true}
 $report.recovered=$recovered
 $readyPath=Join-Path (Split-Path $journal) ('worker-'+$recovered.worker.generation+'.ready')
 $privateUri=(Get-Content -LiteralPath $readyPath -Raw).Trim()
 $fenceStatus=0
 try{Invoke-WebRequest -UseBasicParsing -Uri $privateUri -Headers @{'X-Nsp-Worker-Generation'=$initial.worker.generation} -TimeoutSec 5 | Out-Null}catch{$fenceStatus=[int]$_.Exception.Response.StatusCode}
 Assert ($fenceStatus -eq 409) 'Old generation was not rejected by the fresh worker'
 $report.oldGenerationRejected=$true
 Assert ($recovered.worker.phase -eq 'review_required') 'Unknown operation was incorrectly auto-approved'
 Assert ($recovered.worker.workerPid -ne $initial.worker.workerPid) 'Worker was not replaced'
 Assert (-not (Get-Process -Id $initial.worker.workerPid -ErrorAction SilentlyContinue)) 'Old worker is still alive'
 Assert ((Call-Tool 'nsp_operation_status').serverInstanceId -eq $initial.serverInstanceId) 'Public supervisor restarted during worker recovery'
 $probe=Call-Tool 'nsp_probe_recovery_state'
 Assert $probe.success 'Read-only inspection failed during review'
 $report.inspection=$probe
 $bad=Call-Tool 'nsp_complete_recovery' @{recoveryId='wrong-id';reviewedOperationId=$recovered.worker.interrupted.id;expectedMapTitle=$probe.appState.openTitle;expectedUnsavedChanges=$false;reviewNotes='invalid ID test';confirm=$true}
 Assert (-not $bad.success) 'Stale acknowledgement was accepted'
 Assert ($bad.worker.phase -eq 'review_required') 'Stale acknowledgement confused recovery phase'
 $report.staleAcknowledgement=$bad
 $blockedReview=Call-Tool 'nsp_send_keyboard_shortcut' @{keys='{F4}';confirm=$true}
 Assert ($blockedReview.errorCode -eq 'recovery_required') 'Review gate permitted unverified UI input'
 $report.blockedDuringReview=$blockedReview
 $review=@{recoveryId=$recovered.worker.recoveryId;reviewedOperationId=$recovered.worker.interrupted.id;expectedMapTitle=$probe.appState.openTitle;expectedUnsavedChanges=$probe.appState.hasUnsavedChanges;reviewNotes='Reviewed injected blocked GetAppState only; no calibration input was dispatched. Fresh native/UIA probe confirms same clean offline map and no dialogs. No interrupted request replayed.';confirm=$true}
 $ack=Call-Tool 'nsp_complete_recovery' $review
 Assert ($ack.success -and $ack.worker.phase -eq 'ready') 'Verified recovery did not resume'
 $report.acknowledged=$ack
 # Crash supervisor with a quarantined call; job must kill its worker, journal must survive.
 $again=Call-Tool 'nsp_get_app_state'
 Assert ($again.errorCode -eq 'worker_timeout') 'Second injection did not time out'
 $workerPid=(Call-Tool 'nsp_operation_status').worker.workerPid
 Stop-Test
 Start-Sleep -Milliseconds 500
 Assert (-not (Get-Process -Id $workerPid -ErrorAction SilentlyContinue)) 'Worker survived a hard supervisor exit'
 $restart=Start-Test
 Assert ($restart.worker.phase -eq 'recovery_required') 'Interrupted journal was lost on supervisor restart'
 Assert ($restart.worker.interrupted.id -eq $again.worker.interrupted.id) 'Interrupted intent ID changed across restart'
 $report.afterSupervisorCrash=$restart
 Stop-Test
 # Fake unresponsive probe; never freeze or kill NSP.
 if(Test-Path $journal){Remove-Item -LiteralPath $journal}
 $frozen=Start-Test $true
 $customer=Call-Tool 'nsp_recover_worker' @{confirm=$true}
 Assert ($customer.customerActionRequired -and $customer.worker.phase -eq 'customer_action_required') 'Frozen probe did not request customer action'
 Assert ($customer.worker.nextAction -like '*customer*') 'AI was not told to ask the customer'
 $report.simulatedFrozenNsp=$customer
 $report.nspPidsAfter=@(Get-Process NSP | ForEach-Object {$_.Id})
 Assert (($report.nspPidsBefore -join ',') -eq ($report.nspPidsAfter -join ',')) 'NSP process changed during worker tests'
 $report.originalHashAfter=(Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash
 Assert ($report.originalHashBefore -eq $report.originalHashAfter) 'Original map changed'
 $report.success=$true
}catch{$report.error=$_.Exception.ToString()}finally{
 Stop-Test
 $report.finishedUtc=[DateTime]::UtcNow.ToString('o')
 $report | ConvertTo-Json -Depth 35 | Set-Content "$root\logs\worker-integration-report.json" -Encoding UTF8
}
if(-not $report.success){exit 1}
