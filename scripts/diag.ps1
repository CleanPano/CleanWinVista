# Copia log/config do Nova para docs\diag e lista as janelas do processo (posição, estilos, minimizada?)
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'docs\diag'; New-Item -ItemType Directory -Force $out | Out-Null
$app = Join-Path $env:LOCALAPPDATA 'NovaTaskbar'
Get-ChildItem (Join-Path $app 'logs') -Filter *.log -ErrorAction SilentlyContinue | Copy-Item -Destination $out -Force
Copy-Item (Join-Path $app 'settings.json') $out -Force -ErrorAction SilentlyContinue
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class W {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr h, int i);
  public struct RECT { public int L,T,R,B; }
  public static List<string> List(uint pid) {
    var res = new List<string>();
    EnumWindows((h,l)=>{ uint p; GetWindowThreadProcessId(h,out p); if (p==pid) { RECT r; GetWindowRect(h,out r); var t=new StringBuilder(256); GetWindowText(h,t,256); var c=new StringBuilder(256); GetClassName(h,c,256);
      res.Add(String.Format("{0} '{1}' [{2}] vis={3} min={4} rect=({5},{6})-({7},{8}) {9}x{10} style={11:X} ex={12:X}", h, t, c, IsWindowVisible(h), IsIconic(h), r.L,r.T,r.R,r.B,r.R-r.L,r.B-r.T, GetWindowLongPtrW(h,-16).ToInt64(), GetWindowLongPtrW(h,-20).ToInt64())); } return true; }, IntPtr.Zero);
    return res; } }
'@
$p = Get-Process NovaTaskbar -ErrorAction SilentlyContinue | Select-Object -First 1
$lines = @("process: " + $(if ($p) { "$($p.Id) CPU=$($p.CPU)s WS=$([int]($p.WorkingSet64/1MB))MB Threads=$($p.Threads.Count) Handles=$($p.HandleCount)" } else { 'NAO ESTA RODANDO' }))
if ($p) { $lines += [W]::List([uint32]$p.Id) }
$lines | Set-Content (Join-Path $out 'windows.txt') -Encoding UTF8
$lines
