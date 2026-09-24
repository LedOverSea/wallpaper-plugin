Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class A2 {
  [DllImport("oleacc.dll")]
  public static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint dwId, ref Guid riid, [In, Out, MarshalAs(UnmanagedType.IUnknown)] ref object ppvObject);
  public static object Get(IntPtr h) {
    object o = null;
    Guid g = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
    int hr = AccessibleObjectFromWindow(h, 0xFFFFFFFC, ref g, ref o);
    if (hr != 0) throw new Exception("hr=0x" + hr.ToString("X8"));
    return o;
  }
}
"@

function Invoke-Disp($obj, $dispId, $args) {
  try {
    return $obj.GetType().InvokeMember("", [System.Reflection.BindingFlags]::InvokeMethod, $null, $obj, $args)
  } catch { return "ERR: $($_.Exception.InnerException.Message)" }
}

# PowerShell can call dispinterface members by name through the COM binder:
function Get-Acc($obj, $name, $arg) {
  try {
    if ($null -eq $arg) { return $obj.$name() } else { return $obj.$name($arg) }
  } catch { return "ERR: $($_.Exception.Message)" }
}

foreach ($hwnd in @(0x50C42, 0x40C54, 0xE0D68)) {
  try {
    $o = [A2]::Get([IntPtr]$hwnd)
    Write-Host ("hwnd=0x{0:X}: got object type={1}" -f $hwnd, $o.GetType().FullName)
    $n = Get-Acc $o 'accName' 0
    $c = Get-Acc $o 'accChildCount' $null
    $r = Get-Acc $o 'accRole' 0
    Write-Host ("   name=$n childCount=$c role=$r")
  } catch {
    Write-Host ("hwnd=0x{0:X}: FAIL {1}" -f $hwnd, $_.Exception.Message)
  }
}
