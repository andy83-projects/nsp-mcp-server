# Interactive native WinForms test fixture, never a real ECU/map. Launched only by Test-SettingsControls.ps1.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$main=New-Object Windows.Forms.Form
$main.Text='Haltech NSP - Settings Fixture';$main.Name='MainForm';$main.Size=New-Object Drawing.Size(850,650)
$strip=New-Object Windows.Forms.ToolStrip
$strip.Name='MainToolStrip'
$b=New-Object Windows.Forms.ToolStripButton
$b.Text='Connect/Disconnect (currently disconnected)';$strip.Items.Add($b)|Out-Null
$main.Controls.Add($strip)
$page=New-Object Windows.Forms.Panel
$page.Name='MainPageTab';$page.Dock='Fill';$main.Controls.Add($page);$page.BringToFront()
$heading=New-Object Windows.Forms.Label
$heading.Text='Fixture Settings';$heading.Location=New-Object Drawing.Point(20,50);$heading.AutoSize=$true;$page.Controls.Add($heading)
$accepted=New-Object Windows.Forms.Label
$accepted.Name='AcceptedChoiceLabel';$accepted.Text='A';$accepted.Location=New-Object Drawing.Point(20,100);$accepted.AutoSize=$true;$page.Controls.Add($accepted)
$number=New-Object Windows.Forms.Label
$number.Name='AcceptedNumberLabel';$number.Text='10';$number.Location=New-Object Drawing.Point(20,140);$number.AutoSize=$true;$page.Controls.Add($number)
$open=New-Object Windows.Forms.Button
$open.Name='OpenEditorButton';$open.Text='Edit Fixture Settings';$open.Location=New-Object Drawing.Point(20,180);$open.Size=New-Object Drawing.Size(220,40);$page.Controls.Add($open)
$blocked=New-Object Windows.Forms.Button
$blocked.Name='FileCommandButton';$blocked.Text='Save to File';$blocked.Location=New-Object Drawing.Point(260,180);$blocked.Size=New-Object Drawing.Size(160,40);$page.Controls.Add($blocked)
$blocked.Add_Click({$accepted.Text='COMMAND_EXECUTED'})
$open.Add_Click({
 $dialog=New-Object Windows.Forms.Form
 $dialog.Name='FixtureSettingsDialog';$dialog.Text='Fixture Settings Editor';$dialog.Size=New-Object Drawing.Size(520,380)
 $group=New-Object Windows.Forms.GroupBox
 $group.Text='Choice';$group.Location=New-Object Drawing.Point(20,20);$group.Size=New-Object Drawing.Size(400,90)
 $a=New-Object Windows.Forms.RadioButton
 $a.Name='ChoiceA';$a.Text='Choice A';$a.Location=New-Object Drawing.Point(20,25);$a.Checked=($accepted.Text -eq 'A')
 $c=New-Object Windows.Forms.RadioButton
 $c.Name='ChoiceB';$c.Text='Choice B';$c.Location=New-Object Drawing.Point(180,25);$c.Checked=($accepted.Text -eq 'B')
 $group.Controls.Add($a);$group.Controls.Add($c);$dialog.Controls.Add($group)
 $num=New-Object Windows.Forms.NumericUpDown
 $num.Name='FixtureNumber';$num.Location=New-Object Drawing.Point(20,130);$num.Minimum=0;$num.Maximum=20;$num.Value=[decimal]$number.Text;$dialog.Controls.Add($num)
 $apply=New-Object Windows.Forms.Button
 $apply.Name='ApplyButton';$apply.Text='Apply';$apply.Location=New-Object Drawing.Point(20,230)
 $ok=New-Object Windows.Forms.Button
 $ok.Name='OKButton';$ok.Text='OK';$ok.Location=New-Object Drawing.Point(160,230)
 $cancel=New-Object Windows.Forms.Button
 $cancel.Name='CancelButton';$cancel.Text='Cancel';$cancel.Location=New-Object Drawing.Point(300,230)
 $apply.Add_Click({$accepted.Text=$(if($a.Checked){'A'}else{'B'});$number.Text=$num.Value.ToString()})
 $ok.Add_Click({$accepted.Text=$(if($a.Checked){'A'}else{'B'});$number.Text=$num.Value.ToString();$dialog.Close()})
 $cancel.Add_Click({$dialog.Close()})
 $dialog.Controls.Add($apply);$dialog.Controls.Add($ok);$dialog.Controls.Add($cancel)
 $dialog.ShowDialog($main)|Out-Null;$dialog.Dispose()
})
[Windows.Forms.Application]::Run($main)
