param(
 [string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),
 [string]$ServerDirectory=''
)
# Native controls and independent Apply/OK readback against an isolated fixture.
$ErrorActionPreference='Stop'
$root=(Resolve-Path $RepositoryRoot).Path
if(-not $ServerDirectory){$ServerDirectory=Join-Path $root 'out\server'}
New-Item (Join-Path $root 'logs') -ItemType Directory -Force | Out-Null
$report=[ordered]@{success=$false;startedUtc=[DateTime]::UtcNow.ToString('o');target='Isolated WinForms fixture, not NSP'}
$fixture=$null;$server=$null
function Assert($c,$message){if(-not $c){throw $message}}
function Call($name,$arguments=@{}){
 $body=@{jsonrpc='2.0';id=21;method='tools/call';params=@{name=$name;arguments=$arguments}}|ConvertTo-Json -Depth 25 -Compress
 $r=Invoke-WebRequest -UseBasicParsing -Uri http://127.0.0.1:6278/ -Method Post -ContentType application/json -Headers @{Accept='application/json, text/event-stream'} -Body $body -TimeoutSec 45
 $line=@($r.Content -split "`n"|Where-Object {$_ -like 'data:*'})[0]
 $packet=$line.Substring(5).Trim()|ConvertFrom-Json
 return ($packet.result.content[0].text|ConvertFrom-Json)
}
function Open-Editor {
 $r=Call nsp_open_settings_dialog @{dialogId='FixtureSettingsDialog';expectedTitle='Fixture Settings Editor';expectedPage='Fixture Settings';buttonAutomationId='OpenEditorButton';confirm=$true}
 Assert $r.success ('Fixture dialog did not open: '+($r|ConvertTo-Json -Depth 8 -Compress));return $r
}
function Set-Input($id,$type,$before,$value){
 $r=Call nsp_set_setting @{expectedPage='Fixture Settings Editor';scopeId='FixtureSettingsDialog';automationId=$id;controlType=$type;expectedValue=$before;value=$value;confirm=$true}
 Assert $r.success ('Fixture setting failed: '+($r|ConvertTo-Json -Compress));return $r
}
try{
 $journal="$root\logs\settings-fixture-recovery.json"
 if(Test-Path $journal){Remove-Item $journal}
 $fixture=Start-Process powershell.exe -ArgumentList @('-NoProfile','-STA','-ExecutionPolicy','Bypass','-File',"$root\scripts\Settings-Fixture.ps1") -PassThru
 $serverArgs=@((Join-Path $ServerDirectory 'Nsp.Mcp.Server.dll'),'--Nsp:Server:BindAddress=127.0.0.1','--Nsp:Server:Port=6278','--Nsp:ProcessNameHint=powershell','--Nsp:ExpectedOpenFileName=Settings','--Nsp:WorkerTimeoutSeconds=30',('--Nsp:WorkerJournalPath='+$journal),('--Nsp:AuditLogDirectory='+$root+'\logs\settings-fixture'))
 $server=Start-Process dotnet.exe -ArgumentList $serverArgs -WorkingDirectory $ServerDirectory -WindowStyle Hidden -RedirectStandardOutput "$root\settings-fixture-output.log" -RedirectStandardError "$root\settings-fixture-error.log" -PassThru
 $deadline=[DateTime]::UtcNow.AddSeconds(35)
 do{try{$r=Call nsp_get_app_state;if($r.success){break}}catch{};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $deadline)
 Assert $r.success 'Isolated fixture server failed to become ready'
 $report.fileCommandBlocked=Call nsp_activate_setting_action @{expectedPage='Fixture Settings';buttonAutomationId='FileCommandButton';confirm=$true;verificationValues=@(@{automationId='AcceptedChoiceLabel';controlType='Text';expectedValue='COMMAND_EXECUTED'})}
 Assert ((-not $report.fileCommandBlocked.success) -and (-not $report.fileCommandBlocked.actionAttempted) -and $report.fileCommandBlocked.errorMessage -like '*outside settings*') 'A file command was allowed through the settings action tool'
 $report.open=Open-Editor
 $report.before=Call nsp_list_settings @{scopeId='FixtureSettingsDialog'}
 $report.backgroundWrite=Call nsp_set_setting @{expectedPage='Fixture Settings';automationId='not-a-field';value='1';confirm=$true}
 Assert ((-not $report.backgroundWrite.success) -and $report.backgroundWrite.errorMessage -like '*background*') 'Background page edit was not refused before control lookup'
 $report.dryRun=Call nsp_set_setting @{expectedPage='Fixture Settings Editor';scopeId='FixtureSettingsDialog';automationId='ChoiceB';controlType='RadioButton';expectedValue='false';value='true';confirm=$false}
 Assert ($report.dryRun.success -and $report.dryRun.dryRun) 'Radio dry run failed'
 $report.radioWrite=Set-Input 'ChoiceB' 'RadioButton' 'false' 'true'
 $inv=Call nsp_list_settings @{scopeId='FixtureSettingsDialog'}
 Assert (($inv.controls|Where-Object id -eq ChoiceA).value -eq 'false') 'Selecting radio did not clear its peer'
 $report.radioGroup=$inv
 $report.radioClear=Call nsp_set_setting @{expectedPage='Fixture Settings Editor';scopeId='FixtureSettingsDialog';automationId='ChoiceB';controlType='RadioButton';expectedValue='true';value='false';confirm=$true}
 Assert (-not $report.radioClear.success) 'Independent selected-radio clear was incorrectly accepted'
 $report.staleValue=Call nsp_set_setting @{expectedPage='Fixture Settings Editor';scopeId='FixtureSettingsDialog';automationId='ChoiceB';controlType='RadioButton';expectedValue='false';value='true';confirm=$true}
 Assert (-not $report.staleValue.success) 'Stale expected value was accepted'
 $num=@($inv.controls|Where-Object containerId -eq FixtureNumber)
 Assert ($num.Count -eq 1) 'NumericUpDown compound editor was not identified'
 $numericType=$num[0].type.Replace('ControlType.','')
 $report.numberWrite=Set-Input 'FixtureNumber' $numericType '10' '11'
 $report.pendingMismatch=Call nsp_finish_settings_dialog @{scopeId='FixtureSettingsDialog';expectedTitle='Fixture Settings Editor';action='Apply';buttonAutomationId='ApplyButton';confirm=$true;expectedValues=@(@{automationId='ChoiceB';controlType='RadioButton';expectedValue='false'});verificationPage='Fixture Settings';verificationValues=@(@{automationId='AcceptedChoiceLabel';controlType='Text';expectedValue='B'})}
 Assert (-not $report.pendingMismatch.success) 'Incorrect pending expectation was accepted'
 $pending=@(@{automationId='ChoiceB';controlType='RadioButton';expectedValue='true'},@{automationId='FixtureNumber';controlType=$numericType;expectedValue='11'})
 $verified=@(@{automationId='AcceptedChoiceLabel';controlType='Text';expectedValue='B'},@{automationId='AcceptedNumberLabel';controlType='Text';expectedValue='11'})
 $report.apply=Call nsp_finish_settings_dialog @{scopeId='FixtureSettingsDialog';expectedTitle='Fixture Settings Editor';action='Apply';buttonAutomationId='ApplyButton';confirm=$true;expectedValues=$pending;verificationPage='Fixture Settings';verificationValues=$verified}
 Assert $report.apply.success 'Apply downstream verification failed'
 $report.cancel=Call nsp_finish_settings_dialog @{scopeId='FixtureSettingsDialog';expectedTitle='Fixture Settings Editor';action='Cancel';buttonAutomationId='CancelButton';confirm=$true}
 Assert $report.cancel.success 'Cancel close verification failed'
 Open-Editor|Out-Null
 $inv=Call nsp_list_settings @{scopeId='FixtureSettingsDialog'}
 Assert (($inv.controls|Where-Object id -eq ChoiceB).value -eq 'true') 'Applied radio did not persist after reopening'
 $report.radioRestore=Set-Input 'ChoiceA' 'RadioButton' 'false' 'true'
 $report.numberRestore=Set-Input 'FixtureNumber' $numericType '11' '10'
 $report.ok=Call nsp_finish_settings_dialog @{scopeId='FixtureSettingsDialog';expectedTitle='Fixture Settings Editor';action='OK';buttonAutomationId='OKButton';confirm=$true;expectedValues=@(@{automationId='ChoiceA';controlType='RadioButton';expectedValue='true'},@{automationId='FixtureNumber';controlType=$numericType;expectedValue='10'});verificationPage='Fixture Settings';verificationValues=@(@{automationId='AcceptedChoiceLabel';controlType='Text';expectedValue='A'},@{automationId='AcceptedNumberLabel';controlType='Text';expectedValue='10'})}
 Assert $report.ok.success 'OK downstream verification failed'
 Open-Editor|Out-Null
 Set-Input 'ChoiceB' 'RadioButton' 'false' 'true'|Out-Null
 $report.wrongAcceptedValue=Call nsp_finish_settings_dialog @{scopeId='FixtureSettingsDialog';expectedTitle='Fixture Settings Editor';action='OK';buttonAutomationId='OKButton';confirm=$true;expectedValues=@(@{automationId='ChoiceB';controlType='RadioButton';expectedValue='true'});verificationPage='Fixture Settings';verificationValues=@(@{automationId='AcceptedChoiceLabel';controlType='Text';expectedValue='A'})}
 Assert ((-not $report.wrongAcceptedValue.success) -and $report.wrongAcceptedValue.actionAttempted -and $report.wrongAcceptedValue.nextAction -like '*before retrying*') 'Post-action mismatch was not reported as potentially applied'
 Open-Editor|Out-Null
 $current=Call nsp_list_settings @{scopeId='FixtureSettingsDialog'}
 Assert (($current.controls|Where-Object id -eq ChoiceB).value -eq 'true') 'Post-action failure was incorrectly treated as rollback'
 Set-Input 'ChoiceA' 'RadioButton' 'false' 'true'|Out-Null
 $report.finalRestore=Call nsp_finish_settings_dialog @{scopeId='FixtureSettingsDialog';expectedTitle='Fixture Settings Editor';action='OK';buttonAutomationId='OKButton';confirm=$true;expectedValues=@(@{automationId='ChoiceA';controlType='RadioButton';expectedValue='true'});verificationPage='Fixture Settings';verificationValues=@(@{automationId='AcceptedChoiceLabel';controlType='Text';expectedValue='A'},@{automationId='AcceptedNumberLabel';controlType='Text';expectedValue='10'})}
 Assert $report.finalRestore.success 'Fixture final baseline was not restored'
 $report.success=$true
}catch{$report.error=$_.Exception.ToString()}finally{
 if($server -and -not $server.HasExited){Stop-Process -Id $server.Id -Force;$server.WaitForExit(10000)|Out-Null}
 if($fixture -and -not $fixture.HasExited){Stop-Process -Id $fixture.Id -Force;$fixture.WaitForExit(10000)|Out-Null}
 $report.finishedUtc=[DateTime]::UtcNow.ToString('o')
 $report|ConvertTo-Json -Depth 35|Set-Content "$root\logs\settings-fixture-report.json" -Encoding UTF8
}
if(-not $report.success){exit 1}
