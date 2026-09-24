# install.ps1 - 把 WpeVideoLauncher 装到用户目录(无需管理员权限)
#   - 复制 exe 与 config.json 到 %LOCALAPPDATA%\WpeVideoLauncher
#   - 自动探测 Wallpaper Engine 的 config.json 与 PotPlayer 路径并写回配置
#   - 桌面快捷方式(可选)
#   - 开机自启(写入 HKCU Run, 可选)
# 用法:
#   powershell -ExecutionPolicy Bypass -File install.ps1
#   powershell -ExecutionPolicy Bypass -File install.ps1 -Autostart -NoShortcut
[CmdletBinding()]
param(
    [switch]$Autostart,
    [switch]$NoShortcut,
    [switch]$NoStart,
    [string]$DestinationPath,
    [string]$ShortcutPath
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$here    = Split-Path -Parent $MyInvocation.MyCommand.Path
$exeSrc  = Join-Path $here 'WpeVideoLauncher.exe'
$cfgSrc  = Join-Path $here 'config.json'
$dest    = if ($DestinationPath) { $DestinationPath } else { Join-Path $env:LOCALAPPDATA 'WpeVideoLauncher' }
$exeDst  = Join-Path $dest 'WpeVideoLauncher.exe'
$cfgDst  = Join-Path $dest 'config.json'

if (-not (Test-Path $exeSrc)) { throw "找不到 $exeSrc, 请先运行 build.ps1" }
New-Item -ItemType Directory -Force -Path $dest | Out-Null

# 1) 停掉旧实例
Get-Process WpeVideoLauncher -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "停止旧实例 pid=$($_.Id)"
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 400

# 2) 复制文件
Copy-Item $exeSrc $exeDst -Force
Write-Host "已安装 exe -> $exeDst"

if (-not (Test-Path $cfgDst)) {
    Copy-Item $cfgSrc $cfgDst -Force
    Write-Host "已写入默认配置 -> $cfgDst"
} else {
    Write-Host "保留已有配置 -> $cfgDst"
}

# 3) 自动探测 Wallpaper Engine config.json
function Find-WeConfig {
    foreach ($p in (Get-Process wallpaper64 -ErrorAction SilentlyContinue)) {
        try {
            $dir = Split-Path -Parent $p.MainModule.FileName
            if ($dir) { $c = Join-Path $dir 'config.json'; if (Test-Path $c) { return $c } }
        } catch {}
    }
    $roots = @()
    try {
        $sk = Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue
        if ($sk.SteamPath) { $roots += (Join-Path $sk.SteamPath 'steamapps\common\wallpaper_engine') }
    } catch {}
    $roots += @(
        'D:\program files\steam\steamapps\common\wallpaper_engine',
        'C:\program files\steam\steamapps\common\wallpaper_engine',
        'D:\program files (x86)\steam\steamapps\common\wallpaper_engine',
        'C:\program files (x86)\steam\steamapps\common\wallpaper_engine',
        'D:\steam\steamapps\common\wallpaper_engine',
        'C:\steam\steamapps\common\wallpaper_engine'
    )
    foreach ($r in $roots) { $c = Join-Path $r 'config.json'; if (Test-Path $c) { return $c } }
    return $null
}

$weCfg = Find-WeConfig
if ($weCfg) { Write-Host "Wallpaper Engine config.json: $weCfg" }
else        { Write-Host "Wallpaper Engine config.json: 未找到(可手动填写 config.json 的 configFile)" }

# 4) 自动探测 PotPlayer
function Find-PotPlayer {
    $cands = @()
    foreach ($k in @('HKCU:\SOFTWARE\DAUM\PotPlayerMini64', 'HKCU:\SOFTWARE\DAUM\PotPlayerMini')) {
        try {
            $v = Get-ItemProperty $k -ErrorAction SilentlyContinue
            if ($v) {
                foreach ($name in 'ProgramPath', 'InstallPath', 'Path') {
                    if ($v.$name) { $cands += $v.$name }
                }
            }
        } catch {}
    }
    $cands += @(
        'D:\program files\PotPlayer\PotPlayerMini64.exe',
        'C:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe',
        'D:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe',
        'C:\Program Files (x86)\DAUM\PotPlayer\PotPlayerMini.exe'
    )
    foreach ($c in $cands) {
        $p = ($c -replace '"', '').Trim()
        if ($p -and (Test-Path $p)) { return $p }
    }
    return $null
}

$pot = Find-PotPlayer
if ($pot) { Write-Host "播放器: $pot" } else { Write-Host "播放器: 未找到(可手动填写 config.json 的 player)" }

# 5) 回写配置(用正斜杠, 避免 JSON 转义)
$cfg = Get-Content $cfgDst -Raw -Encoding UTF8 | ConvertFrom-Json
if ($weCfg) { $cfg.configFile = ($weCfg -replace '\\', '/') }
if ($pot)   { $cfg.player     = ($pot   -replace '\\', '/') }
if (-not $cfg.videoExtensions) {
    $cfg | Add-Member -NotePropertyName videoExtensions -NotePropertyValue '.mp4,.webm,.mkv,.avi,.mov,.m4v,.wmv,.flv,.mpg,.mpeg,.ts' -Force
}
$cfg | ConvertTo-Json -Depth 5 | Set-Content $cfgDst -Encoding UTF8
Write-Host "配置已更新 -> $cfgDst"

# 6) 桌面快捷方式
if (-not $NoShortcut) {
    $lnk = if ($ShortcutPath) { $ShortcutPath } else { Join-Path ([Environment]::GetFolderPath('Desktop')) '播放当前壁纸视频.lnk' }
    $ws = New-Object -ComObject WScript.Shell
    $sc = $ws.CreateShortcut($lnk)
    $sc.TargetPath = $exeDst
    $sc.Arguments = '--once'
    $sc.WorkingDirectory = $dest
    $sc.Description = '用 PotPlayer 播放当前 Wallpaper Engine 壁纸的视频'
    $sc.IconLocation = "$exeDst,0"
    $sc.Save()
    Write-Host "已创建桌面快捷方式 -> $lnk"
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($ws)
}

# 7) 开机自启
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ($Autostart) {
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name 'WpeVideoLauncher' -Value ('"' + $exeDst + '"')
    Write-Host "已设置开机自启 (HKCU Run)"
} else {
    Write-Host "未设置开机自启 (如需: install.ps1 -Autostart, 或把快捷方式放进 shell:startup)"
}

# 8) 启动
if (-not $NoStart) {
    Start-Process -FilePath $exeDst -WorkingDirectory $dest
    Write-Host "已启动, 请看托盘图标 (蓝色圆形 + 播放三角)"
}

Write-Host ""
Write-Host "完成。提示:"
Write-Host "  * 热键默认 Ctrl+Alt+P (在 config.json 的 hotkey 里改, 设成 none 则关闭)"
Write-Host "  * 托盘图标双击 = 立即播放当前壁纸视频"
Write-Host "  * 自检: 运行 `"$exeDst`" --diagnose , 然后查看 $dest\launcher.log"
