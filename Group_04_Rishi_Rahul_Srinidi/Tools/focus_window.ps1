param([string]$proc = "MetaXRSimulator")
# Brings the main window of the named process to the foreground (restoring it if minimised).
Add-Type @"
using System; using System.Runtime.InteropServices;
public class Fg {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
"@
$p = Get-Process -Name $proc -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { "no window for $proc"; exit 1 }
[Fg]::ShowWindow($p.MainWindowHandle, 9) | Out-Null
# an Alt tap lets SetForegroundWindow succeed from a background process
[Fg]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [Fg]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
[Fg]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
"focused $($p.ProcessName) $($p.Id) '$($p.MainWindowTitle)'"
