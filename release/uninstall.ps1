# uninstall.ps1 - 卸载 WpeVideoLauncher (只删自己装的东西, 不动 Wallpaper Engine)
# 用法: powershell -ExecutionPolicy Bypass -File uninstall.ps1
[CmdletBinding()]
param(
    [switch]$KeepConfig,
    [string]$DestinationPath,
    [string]$ShortcutPath
)

$ErrorActionPreference = 'Continue'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$dest   = if ($DestinationPath) { $DestinationPath } else { Join-Path $env:LOCALAPPDATA 'WpeVideoLauncher' }

# 1) 结束进程
Get-Process WpeVideoLauncher -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "结束进程 pid=$($_.Id)"
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 400

# 2) 开机自启
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
try {
    if (Get-ItemProperty -Path $runKey -Name 'WpeVideoLauncher' -ErrorAction SilentlyContinue) {
        Remove-ItemProperty -Path $runKey -Name 'WpeVideoLauncher' -Force
        Write-Host "已移除开机自启"
    }
} catch {}

# 3) 桌面快捷方式
$lnk = if ($ShortcutPath) { $ShortcutPath } else { Join-Path ([Environment]::GetFolderPath('Desktop')) '播放当前壁纸视频.lnk' }
if (Test-Path $lnk) { Remove-Item $lnk -Force; Write-Host "已删除桌面快捷方式" }

# 4) 安装目录
if (Test-Path $dest) {
    if ($KeepConfig) {
        Get-ChildItem $dest -Exclude 'config.json', 'launcher.log' | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "已删除程序文件, 保留 $dest\config.json"
    } else {
        Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "已删除 $dest"
    }
}

Write-Host "卸载完成。Wallpaper Engine 本体未做任何改动。"
