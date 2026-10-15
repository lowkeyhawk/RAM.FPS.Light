$source = @"
using System;
using System.Runtime.InteropServices;
public class RtssReader {
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenFileMapping(int access, bool inherit, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr MapViewOfFile(IntPtr handle, int access, int offsetHigh, int offsetLow, int bytes);
    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr handle);
}
"@
Add-Type -TypeDefinition $source

$h = [RtssReader]::OpenFileMapping(0xF0001, $false, "RTSSSharedMemoryV2")
if ($h -eq [IntPtr]::Zero) { Write-Host "RTSS shared memory not found"; exit }
$ptr = [RtssReader]::MapViewOfFile($h, 0xF0001, 0, 0, 4096)
if ($ptr -eq [IntPtr]::Zero) { Write-Host "Map failed"; exit }

$sig = [Marshal]::ReadInt32($ptr, 0)
Write-Host ("Signature: 0x{0:X8}" -f $sig)
$ver = [Marshal]::ReadInt32($ptr, 4)
Write-Host ("Version: {0}" -f $ver)
$appOff = [Marshal]::ReadInt32($ptr, 8)
Write-Host ("AppEntryOffset: {0}" -f $appOff)
$appSize = [Marshal]::ReadInt32($ptr, 12)
Write-Host ("AppEntrySize: {0}" -f $appSize)
$appCount = [Marshal]::ReadInt32($ptr, 16)
Write-Host ("AppCount: {0}" -f $appCount)

if ($appCount -gt 0 -and $appOff -gt 0) {
    Write-Host "App entry dump:"
    for ($i = 0; $i -lt 20; $i++) {
        $val = [Marshal]::ReadInt32($ptr, $appOff + ($i * 4))
        Write-Host ("  offset {0}: {1}" -f ($appOff + ($i * 4)), $val)
    }
} else {
    Write-Host "No apps hooked or invalid offsets"
}
[RtssReader]::CloseHandle($h)
