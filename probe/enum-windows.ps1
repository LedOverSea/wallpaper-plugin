Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;

public class W {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int m);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int m);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);

  public static string Info(IntPtr h) {
    var c = new StringBuilder(256); GetClassName(h, c, 256);
    var t = new StringBuilder(512); GetWindowText(h, t, 512);
    uint pid; GetWindowThreadProcessId(h, out pid);
    return string.Format("hwnd=0x{0:X} pid={1} vis={2} style=0x{3:X} class='{4}' title='{5}'",
      h.ToInt64(), pid, IsWindowVisible(h), GetWindowLong(h, -16), c.ToString(), t.ToString());
  }
}
"@

$targetPids = (Get-Process | Where-Object { $_.ProcessName -match 'wallpaper' }).Id
Write-Host "wallpaper pids: $($targetPids -join ', ')"

$rows = New-Object System.Collections.Generic.List[string]
$cb = [W+EnumProc]{
  param($h, $l)
  $pid2 = 0
  [void][W]::GetWindowThreadProcessId($h, [ref]$pid2)
  $script:all += ,@($h, $pid2)
  return $true
}
$script:all = @()
[void][W]::EnumWindows($cb, [IntPtr]::Zero)
Write-Host "top-level windows total: $($script:all.Count)"

foreach ($e in $script:all) {
  if ($targetPids -contains $e[1]) {
    Write-Host ("TOP  " + [W]::Info($e[0]))
    $script:kids = @()
    $cb2 = [W+EnumProc]{
      param($h, $l)
      $p3 = 0
      [void][W]::GetWindowThreadProcessId($h, [ref]$p3)
      $script:kids += ,@($h, $p3)
      return $true
    }
    [void][W]::EnumChildWindows($e[0], $cb2, [IntPtr]::Zero)
    foreach ($k in $script:kids) { Write-Host ("   CHILD " + [W]::Info($k[0])) }
  }
}
