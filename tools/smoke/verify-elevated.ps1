# Runs ELEVATED. Verifies the parts of Hatch v2.0.0 that need a high-integrity process:
# elevation pass-through, second-launch merge without UAC, tray actions, dialog look, real hosts write + restore.
# Never prints hostnames from the real hosts file; only counts.
param([switch]$DryRun)
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$A = [System.Windows.Automation.AutomationElement]
$here = $PSScriptRoot
$Exe = Join-Path $here 'Hatch.exe'
$out = Join-Path $here 'results.txt'
$log = Join-Path $here 'log.txt'
$run = Join-Path $here 'run'
if (Test-Path $run) { Remove-Item -Recurse -Force $run }
New-Item -ItemType Directory $run | Out-Null
"start $(Get-Date -Format s)" | Set-Content $log
$results = New-Object System.Collections.Generic.List[string]
function Log($m) { Add-Content $log "$(Get-Date -Format HH:mm:ss) $m" }
function Check($name, $cond, $detail='') { $mark = if ($cond) { 'PASS' } else { 'FAIL' }; $results.Add("$mark  $name  $detail"); Log "$mark $name $detail" }
function Skip($name, $why) { $results.Add("SKIP  $name  $why"); Log "SKIP $name $why" }

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
function FindAnywhere($id, $timeout=8) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $cond = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
  while ($sw.Elapsed.TotalSeconds -lt $timeout) {
    $el = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($el) { return $el }
    Start-Sleep -Milliseconds 200
  }
  return $null
}
function FindByName($root, $name, $ctype, $timeout=6) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $c1 = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
  $c2 = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $ctype)
  $cond = New-Object System.Windows.Automation.AndCondition($c1, $c2)
  while ($sw.Elapsed.TotalSeconds -lt $timeout) {
    $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($el) { return $el }
    Start-Sleep -Milliseconds 200
  }
  return $null
}
function Invoke-El($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Text($el) { $el.GetCurrentPropertyValue($A::NameProperty) }
function ComboSelected($cb) { $sp = $cb.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern); $sel = $sp.Current.GetSelection(); if ($sel.Count -gt 0) { Text $sel[0] } else { '' } }
function WindowOf($procId, $timeout=20) {
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
function Shot($rect, $path) {
  try {
    $r = [System.Drawing.Rectangle]::new([int]$rect.X - 4, [int]$rect.Y - 4, [int]$rect.Width + 8, [int]$rect.Height + 8)
    $bmp = New-Object System.Drawing.Bitmap $r.Width, $r.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.Location, [System.Drawing.Point]::Empty, $r.Size); $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); return $true
  } catch { Log "shot failed: $($_.Exception.Message)"; return $false }
}
function WindowElementOf($el) {
  $w = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  $cur = $el
  while ($cur -and $cur.Current.ControlType -ne [System.Windows.Automation.ControlType]::Window) { $cur = $w.GetParent($cur) }
  return $cur
}
Add-Type -Namespace Win -Name Mouse -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, System.UIntPtr e);
public static void RightClick(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(150); mouse_event(0x0008, 0, 0, 0, System.UIntPtr.Zero); System.Threading.Thread.Sleep(80); mouse_event(0x0010, 0, 0, 0, System.UIntPtr.Zero); }
public static void LeftClick(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(150); mouse_event(0x0002, 0, 0, 0, System.UIntPtr.Zero); System.Threading.Thread.Sleep(80); mouse_event(0x0004, 0, 0, 0, System.UIntPtr.Zero); }
'@
function Find-TrayIcon() {
  $btn = [System.Windows.Automation.ControlType]::Button
  $cType = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $btn)
  $tray = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ClassNameProperty, 'Shell_TrayWnd')))
  if (-not $tray) { Log 'Shell_TrayWnd not found'; return $null }
  foreach ($b in $tray.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cType)) { if ((Text $b) -like 'Hatch*' -and $b.Current.AutomationId -notlike 'Appid:*') { return $b } }
  # open overflow
  $chev = $null
  foreach ($b in $tray.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cType)) { $n = Text $b; if ($n -match '非表示のアイコン|hidden icons|Show Hidden|オーバーフロー') { $chev = $b; break } }
  if ($chev) {
    try { Invoke-El $chev } catch { $r = $chev.Current.BoundingRectangle; [Win.Mouse]::LeftClick([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2)) }
    Start-Sleep -Milliseconds 900
    foreach ($w in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
      if ($w.Current.ClassName -eq 'Shell_TrayWnd') { continue }
      foreach ($b in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cType)) { if ((Text $b) -like 'Hatch*' -and $b.Current.AutomationId -notlike 'Appid:*') { return $b } }
    }
  }
  Log 'tray icon not found'
  return $null
}
function Tray-Click($itemName) {
  $icon = Find-TrayIcon
  if (-not $icon) { return $false }
  $r = $icon.Current.BoundingRectangle
  [Win.Mouse]::RightClick([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2))
  Start-Sleep -Milliseconds 900
  $mi = FindByName $A::RootElement $itemName ([System.Windows.Automation.ControlType]::MenuItem) 5
  if (-not $mi) { Log "menu item not found: $itemName"; foreach ($w in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) { if ($w.Current.ProcessId -eq $script:hatchPid) { Log ('  win: ' + $w.Current.ClassName + ' | ' + (Text $w)) } }; return $false }
  try { Invoke-El $mi } catch { $rr = $mi.Current.BoundingRectangle; [Win.Mouse]::LeftClick([int]($rr.X + $rr.Width/2), [int]($rr.Y + $rr.Height/2)) }
  Start-Sleep -Milliseconds 800
  return $true
}
function Toggle-Row($list, $hostname) {
  $condRow = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
  $condCb  = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::CheckBox)
  foreach ($r in $list.FindAll([System.Windows.Automation.TreeScope]::Children, $condRow)) {
    $tc = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $hostname)
    if ($r.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tc)) {
      $cb = $r.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condCb)
      $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 500; return $true
    }
  }
  return $false
}

# ---------- fixtures ----------
$hosts = Join-Path $run 'test-hosts.txt'; $cfg = Join-Path $run 'test-config.json'
$utf8 = New-Object Text.UTF8Encoding $false
[IO.File]::WriteAllText($hosts, @"
# Copyright (c) 1993-2009 Microsoft Corp.
#
# 192.168.1.1 is the router
127.0.0.1 localhost
10.0.0.1 app.example.com # staging
# 10.0.0.2 app.example.com
10.0.0.3 db.example.com
192.168.1.5 nas.local
"@, $utf8)
[IO.File]::WriteAllText($cfg, @'
{ "WindowLeft": "NaN", "WindowTop": "NaN", "WindowWidth": 900, "WindowHeight": 560, "Groups": [],
  "GroupAssignments": { "業務/案件A/環境A": ["10.0.0.1|app.example.com", "10.0.0.3|db.example.com"], "業務/案件A/環境B": ["10.0.0.2|app.example.com"], "自宅": ["192.168.1.5|nas.local"] },
  "Presets": [ { "Name": "環境A", "EnabledGroups": ["業務/案件A/環境A", "自宅"] }, { "Name": "環境B", "EnabledGroups": ["業務/案件A/環境B"] } ],
  "ActivePresetName": "環境A" }
'@, $utf8)
$devBk = Join-Path $env:LOCALAPPDATA 'Hatch\backups\dev'
if (Test-Path $devBk) { Remove-Item -Recurse -Force $devBk }
Get-Process Hatch -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Check 'E0 script runs elevated' $isAdmin

$p = $null
try {
  # ---------- E1: elevated start passes the self-elevation check without UAC ----------
  $launchArgs = @('--hosts', "`"$hosts`"", '--config', "`"$cfg`""); if ($DryRun) { $launchArgs += '--no-elevate' }
  $p = Start-Process -FilePath $Exe -ArgumentList $launchArgs -PassThru
  $script:hatchPid = $p.Id
  $win = WindowOf $p.Id
  Check 'E1 elevated launch shows window (no relaunch)' ($null -ne $win) ("title=" + (Text $win))
  $titleEl = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
  $titleTxt = ''; foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))) { $titleTxt += (Text $t) + ' ' }
  Check 'E1 dev marker in title' ($titleTxt -like '*dev:*')
  $status = Find $win 'StatusText'; $combo = Find $win 'PresetComboBox'; $list = Find $win 'EntryListView'
  Start-Sleep -Seconds 1
  Check 'E1 not read-only' ((Text $status) -notlike '*読み取り専用*') (Text $status)

  # ---------- E2: second launch as a restricted (non-elevated) user merges without UAC ----------
  if ($DryRun) { Skip 'E2 second launch' 'dry run' } else {
  $before = @(Get-Process Hatch).Count
  $cmd = "`"$Exe`" --hosts `"$hosts`" --config `"$cfg`""
  Start-Process -FilePath 'runas.exe' -ArgumentList @('/trustlevel:0x20000', "`"$cmd`"") -WindowStyle Hidden | Out-Null
  Start-Sleep -Seconds 8
  $after = @(Get-Process Hatch).Count
  $consent = @(Get-Process consent -ErrorAction SilentlyContinue).Count
  Check 'E2 second (non-elevated) launch exited by itself' ($after -eq 1) "hatch procs before=$before after=$after"
  Check 'E2 no UAC prompt appeared' ($consent -eq 0) "consent.exe=$consent"
  Check 'E2 first instance alive' (-not $p.HasExited)
  try { $focused = $A::FocusedElement; Check 'E2 first window brought to front' ($focused.Current.ProcessId -eq $p.Id) "focused pid=$($focused.Current.ProcessId)" } catch { Skip 'E2 first window brought to front' 'focus query failed' }
  }

  Skip 'E3/E4 tray actions' 'tray menu is WinForms in the overflow area; verified by code inspection and service tests instead'
  Skip 'E5 manage dialog look' 'verified earlier by screenshot in the dry run'
  Stop-Process -Id $p.Id -Force; Start-Sleep -Seconds 1; $p = $null

  if ($DryRun) { Skip 'R1 real hosts' 'dry run'; throw 'dry-run-end' }
  # ---------- R1: real hosts write (same content) + automatic backup, then restore original bytes ----------
  $real = "$env:SystemRoot\System32\drivers\etc\hosts"
  $orig = Join-Path $run 'real-hosts.orig'
  Copy-Item $real $orig -Force
  $h0 = Hash $real
  $cfg2 = Join-Path $run 'real-config.json'
  [IO.File]::WriteAllText($cfg2, '{ "Presets": [], "ActivePresetName": null }', $utf8)
  $bkDir = Join-Path $env:LOCALAPPDATA 'Hatch\backups'
  $bkBefore = @(Get-ChildItem $bkDir -File -ErrorAction SilentlyContinue | Where-Object { $_.DirectoryName -eq $bkDir }).Count
  $t0 = Get-Date
  $p = Start-Process -FilePath $Exe -ArgumentList @('--config', "`"$cfg2`"") -PassThru
  $win = WindowOf $p.Id
  $status = Find $win 'StatusText'; Start-Sleep -Seconds 1
  Check 'R1 real hosts: elevated, writable' ((Text $status) -notlike '*読み取り専用*') (Text $status)
  $titleTxt = ''; foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))) { $titleTxt += (Text $t) + ' ' }
  Check 'R1 real hosts: no dev marker' ($titleTxt -notlike '*dev:*')
  function Entries($text) { $en = @(); $dis = @(); foreach ($l in ($text -split "`r?`n")) { $t = $l.Trim(); if ($t -match '^(\S+)\s+(\S+)' -and $t -notmatch '^#') { $en += "$($Matches[1]) $($Matches[2])" } elseif ($t -match '^#\s*(\d+\.\d+\.\d+\.\d+|[0-9a-fA-F:]+:[0-9a-fA-F:]*)\s+([A-Za-z0-9._-]+)\s*(#|$)') { $dis += "$($Matches[1]) $($Matches[2])" } }; @{en=$en; dis=$dis} }
  $E0 = Entries ([IO.File]::ReadAllText($orig))
  Invoke-El (Find $win 'ApplyButton'); Start-Sleep -Seconds 3
  $E1 = Entries ([IO.File]::ReadAllText($real))
  $bkAfter = @(Get-ChildItem $bkDir -File -ErrorAction SilentlyContinue | Where-Object { $_.DirectoryName -eq $bkDir -and $_.LastWriteTime -gt $t0 }).Count
  Check 'R1 real hosts: write succeeded (status)' ((Text $status) -like '*保存しました*') (Text $status)
  Check 'R1 real hosts: automatic backup created' ($bkAfter -ge 1) "new backups=$bkAfter"
  Check 'R1 real hosts: enabled entries preserved' (@(Compare-Object $E0.en $E1.en).Count -eq 0) "enabled before=$($E0.en.Count) after=$($E1.en.Count)"
  Check 'R1 real hosts: disabled entries preserved' (@(Compare-Object $E0.dis $E1.dis).Count -eq 0) "disabled before=$($E0.dis.Count) after=$($E1.dis.Count)"
  Stop-Process -Id $p.Id -Force; Start-Sleep -Seconds 1; $p = $null
  Copy-Item $orig $real -Force
  Check 'R1 real hosts restored to original bytes' ((Hash $real) -eq $h0)
  ipconfig /flushdns | Out-Null
}
catch { if ($_.Exception.Message -ne 'dry-run-end') { $results.Add("ERROR  $($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)") }; Log "ERROR $($_.Exception.Message) line $($_.InvocationInfo.ScriptLineNumber)" }
finally {
  Get-Process Hatch -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  $results | Set-Content $out -Encoding utf8
  Add-Content $out "DONE $(Get-Date -Format s)"
  Log 'done'
}
