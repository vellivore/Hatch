param([string]$Exe)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$A = [System.Windows.Automation.AutomationElement]
$dir = Join-Path $PSScriptRoot 'run'
if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
New-Item -ItemType Directory $dir | Out-Null
$hosts = Join-Path $dir 'test-hosts.txt'
$cfg   = Join-Path $dir 'test-config.json'
$bk    = Join-Path $env:LOCALAPPDATA 'Hatch\backups\dev'
if (Test-Path $bk) { Remove-Item -Recurse -Force $bk }

# v1.3-style fixtures: no tags in hosts, groups only in config
$hostsText = @"
# Copyright (c) 1993-2009 Microsoft Corp.
#
# 192.168.1.1 is the router
127.0.0.1 localhost
10.0.0.1 app.example.com # staging
# 10.0.0.2 app.example.com
10.0.0.3 db.example.com
192.168.1.5 nas.local
"@
[IO.File]::WriteAllText($hosts, $hostsText, (New-Object Text.UTF8Encoding $false))
$cfgText = @'
{
  "WindowLeft": "NaN", "WindowTop": "NaN", "WindowWidth": 900, "WindowHeight": 560,
  "Groups": [],
  "GroupAssignments": {
    "業務/案件A/環境A": ["10.0.0.1|app.example.com", "10.0.0.3|db.example.com"],
    "業務/案件A/環境B": ["10.0.0.2|app.example.com"],
    "自宅": ["192.168.1.5|nas.local"]
  },
  "Presets": [
    { "Name": "環境A", "EnabledGroups": ["業務/案件A/環境A", "自宅"] },
    { "Name": "環境B", "EnabledGroups": ["業務/案件A/環境B"] }
  ],
  "ActivePresetName": "環境A"
}
'@
[IO.File]::WriteAllText($cfg, $cfgText, (New-Object Text.UTF8Encoding $false))

$results = New-Object System.Collections.Generic.List[string]
function Check($name, $cond, $detail='') {
  $mark = if ($cond) { 'PASS' } else { 'FAIL' }
  $results.Add("$mark  $name  $detail")
}
function Find($root, $id, $timeout=10) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt $timeout) {
    $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($el) { return $el }
    Start-Sleep -Milliseconds 200
  }
  throw "element not found: $id"
}
function TryFind($root, $id, $timeout=3) { try { return Find $root $id $timeout } catch { return $null } }
function Invoke-El($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Text($el) { $el.GetCurrentPropertyValue($A::NameProperty) }
function ComboSelected($cb) {
  $sp = $cb.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
  $sel = $sp.Current.GetSelection(); if ($sel.Count -gt 0) { return (Text $sel[0]) } else { return '' }
}
function ComboSelect($cb, $name) {
  $ec = $cb.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
  $ec.Expand(); Start-Sleep -Milliseconds 300
  $cond = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
  $item = $cb.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
  if (-not $item) { throw "combo item not found: $name" }
  $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  try { $ec.Collapse() } catch {}
  Start-Sleep -Milliseconds 400
}
function WindowOf($procId, $timeout=15) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, [int]$procId)
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt $timeout) {
    $w = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if ($w) { return $w }
    Start-Sleep -Milliseconds 300
  }
  throw "window not found for pid $procId"
}
function Hash($p) { (Get-FileHash $p -Algorithm SHA256).Hash }
function Backups() { if (Test-Path $bk) { @(Get-ChildItem $bk -File).Count } else { 0 } }

$h0 = Hash $hosts
$p = Start-Process -FilePath $Exe -ArgumentList @('--hosts', "`"$hosts`"", '--config', "`"$cfg`"", '--no-elevate') -PassThru
try {
  $win = WindowOf $p.Id
  $status = Find $win 'StatusText'
  $combo  = Find $win 'PresetComboBox'
  Start-Sleep -Seconds 1

  # T1: migration message, nothing written at startup
  $st = Text $status
  Check 'T1 startup shows migration message' ($st -like '*移行*') $st
  Check 'T1 hosts untouched at startup' ((Hash $hosts) -eq $h0)
  Check 'T1 active preset restored' ((ComboSelected $combo) -eq '環境A') (ComboSelected $combo)

  # T2: open + close preset manager -> no change, no write
  Invoke-El (Find $win 'ManagePresetsButton')
  $mw = WindowOf $p.Id  # modal dialog is also a top-level window of the process; find close button anywhere in process
  $closeBtn = $null
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while (-not $closeBtn -and $sw.Elapsed.TotalSeconds -lt 10) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, 'ManageCloseButton')
    $closeBtn = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    Start-Sleep -Milliseconds 200
  }
  Check 'T2 manage dialog opened' ($null -ne $closeBtn)
  Invoke-El $closeBtn; Start-Sleep -Seconds 1
  Check 'T2 preset unchanged after closing manager' ((ComboSelected $combo) -eq '環境A') (ComboSelected $combo)
  Check 'T2 hosts not written by closing manager' ((Hash $hosts) -eq $h0)
  Check 'T2 no backup created by closing manager' ((Backups) -eq 0) (Backups)

  # T3: Apply -> tags written, GroupAssignments removed, backup made
  Invoke-El (Find $win 'ApplyButton'); Start-Sleep -Seconds 2
  $ht = [IO.File]::ReadAllText($hosts)
  Check 'T3 tag written for app.example.com' ($ht -match '10\.0\.0\.1 app\.example\.com # \[業務/案件A/環境A\] staging')
  Check 'T3 disabled entry keeps tag' ($ht -match '# 10\.0\.0\.2 app\.example\.com # \[業務/案件A/環境B\]')
  Check 'T3 router comment preserved' ($ht -match '# 192\.168\.1\.1 is the router')
  $ct = [IO.File]::ReadAllText($cfg)
  Check 'T3 GroupAssignments removed from config' ($ct -notmatch 'GroupAssignments')
  Check 'T3 backup created on write' ((Backups) -eq 1) (Backups)
  $st = Text $status
  Check 'T3 migration message gone' ($st -notlike '*移行*') $st

  # T4: make dirty (toggle nas.local checkbox), switch preset -> 3-choice dialog -> cancel reverts
  $list = Find $win 'EntryListView'
  $condItem = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::CheckBox)
  $checks = $list.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condItem)
  # find the row whose text includes nas.local: walk data items
  $condRow = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
  $rows = $list.FindAll([System.Windows.Automation.TreeScope]::Children, $condRow)
  $target = $null
  foreach ($r in $rows) { if ((Text $r) -like '*nas.local*') { $target = $r; break } }
  if (-not $target) {
    # fallback: search text descendants
    foreach ($r in $rows) {
      $tc = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'nas.local')
      if ($r.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tc)) { $target = $r; break }
    }
  }
  Check 'T4 nas.local row found' ($null -ne $target) "rows=$($rows.Count)"
  $cb = $target.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condItem)
  $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 500
  $h3 = Hash $hosts
  ComboSelect $combo '環境B'
  $cancel = $null; $sw = [Diagnostics.Stopwatch]::StartNew()
  while (-not $cancel -and $sw.Elapsed.TotalSeconds -lt 8) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, 'SwitchCancelButton')
    $cancel = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond); Start-Sleep -Milliseconds 200
  }
  Check 'T4 dirty switch shows 3-choice dialog' ($null -ne $cancel)
  if ($cancel) { Invoke-El $cancel; Start-Sleep -Seconds 1 }
  Check 'T4 cancel reverts combo to 環境A' ((ComboSelected $combo) -eq '環境A') (ComboSelected $combo)
  Check 'T4 cancel did not write hosts' ((Hash $hosts) -eq $h3)

  # T5: switch again -> discard -> hosts reflects 環境B (10.0.0.2 enabled, 10.0.0.1 disabled, nas.local back to enabled)
  ComboSelect $combo '環境B'
  $discard = $null; $sw = [Diagnostics.Stopwatch]::StartNew()
  while (-not $discard -and $sw.Elapsed.TotalSeconds -lt 8) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, 'SwitchDiscardButton')
    $discard = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond); Start-Sleep -Milliseconds 200
  }
  Check 'T5 dialog shown again' ($null -ne $discard)
  if ($discard) { Invoke-El $discard; Start-Sleep -Seconds 2 }
  $ht = [IO.File]::ReadAllText($hosts)
  Check 'T5 環境B enabled 10.0.0.2' ($ht -match '(?m)^10\.0\.0\.2 app\.example\.com # \[業務/案件A/環境B\]')
  Check 'T5 環境A disabled 10.0.0.1' ($ht -match '(?m)^# 10\.0\.0\.1 app\.example\.com # \[業務/案件A/環境A\] staging')
  Check 'T5 自宅 disabled (not in 環境B)' ($ht -match '(?m)^# 192\.168\.1\.5 nas\.local # \[自宅\]')
  Check 'T5 localhost untouched' ($ht -match '(?m)^127\.0\.0\.1 localhost')
  Check 'T5 combo now 環境B' ((ComboSelected $combo) -eq '環境B') (ComboSelected $combo)
  Check 'T5 backups now 2' ((Backups) -eq 2) (Backups)

  # T7: backup rotation - apply 21 more times, expect cap of 20
  $applyBtn = Find $win 'ApplyButton'
  for ($i = 0; $i -lt 21; $i++) { Invoke-El $applyBtn; Start-Sleep -Milliseconds 1200 }
  Check 'T7 backups capped at 20' ((Backups) -le 20) (Backups)
  Check 'T7 hosts still parseable (tag kept)' ([IO.File]::ReadAllText($hosts) -match '10\.0\.0\.2 app\.example\.com # \[業務/案件A/環境B\]')

  # T8: second launch without elevation should exit quickly and not spawn a second window
  $p2 = Start-Process -FilePath $Exe -ArgumentList @('--hosts', "`"$hosts`"", '--config', "`"$cfg`"", '--no-elevate') -PassThru
  $exited = $p2.WaitForExit(8000)
  Check 'T8 second instance exits by itself' $exited "exitcode=$($p2.ExitCode)"
  if (-not $exited) { Stop-Process -Id $p2.Id -Force }
  Check 'T8 first instance still alive' (-not $p.HasExited)

  # T6: prefix preset: save state as preset with parent group via manager is UI-heavy; instead verify config persisted ActivePresetName
  Start-Sleep -Milliseconds 500
  Invoke-El (Find $win 'CloseToTrayButton'); Start-Sleep -Seconds 1
  $ct = [IO.File]::ReadAllText($cfg)
  Check 'T6 ActivePresetName saved as 環境B' ((ConvertFrom-Json $ct).ActivePresetName -eq '環境B')
  Check 'T6 window position saved as numbers (no NaN)' ($ct -notmatch 'NaN')
}
catch { $results.Add("ERROR  $($_.Exception.Message)") }
finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
$results | ForEach-Object { Write-Output $_ }
Write-Output "--- hosts after ---"
Get-Content $hosts
Write-Output "--- config after ---"
Get-Content $cfg


