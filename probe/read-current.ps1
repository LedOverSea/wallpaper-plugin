# 探查: 从 WE config.json 读出当前壁纸 -> 视频文件绝对路径
$cfgPath = 'D:\program files\steam\steamapps\common\wallpaper_engine\config.json'
$cfg = Get-Content -LiteralPath $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json

function Get-WECurrent {
    param($Cfg, [string]$Monitor = 'Monitor0')
    # config.json 顶层是 { "<steamid>" : { ... } }
    foreach ($userProp in $Cfg.PSObject.Properties) {
        $user = $userProp.Value
        if ($null -eq $user.wallpaperconfig) { continue }
        $sel = $user.wallpaperconfig.selectedwallpapers
        if ($null -eq $sel) { continue }
        $m = $sel.PSObject.Properties | Where-Object { $_.Name -eq $Monitor }
        if ($null -eq $m) { $m = $sel.PSObject.Properties | Select-Object -First 1 }
        if ($null -eq $m) { continue }
        return [pscustomobject]@{
            User    = $userProp.Name
            Monitor = $m.Name
            File    = $m.Value.file
            Empty   = $m.Value.PSObject.Properties.Name -contains 'empty'
            Title   = $user.wallpaperconfigrecent[0].title
        }
    }
    return $null
}

Write-Host "=== 当前壁纸 ==="
$cur = Get-WECurrent $cfg
$cur | Format-List

if ($cur -and $cur.File) {
    $norm = $cur.File -replace '/', '\'
    Write-Host "规范化路径 : $norm"
    Write-Host "文件存在   : $(Test-Path -LiteralPath $norm)"
    if (Test-Path -LiteralPath $norm) {
        $fi = Get-Item -LiteralPath $norm
        Write-Host "大小(MB)   : $([math]::Round($fi.Length/1MB,1))"
        Write-Host "扩展名     : $($fi.Extension)"
        Write-Host "所在目录   : $($fi.DirectoryName)"
        Write-Host "=== 目录内视频候选 ==="
        Get-ChildItem -LiteralPath $fi.DirectoryName -File |
            Where-Object { $_.Extension -match '^\.(mp4|webm|mkv|avi|mov|m4v|wmv|flv)$' } |
            Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
    }
}

Write-Host "=== 所有显示器的 selectedwallpapers ==="
foreach ($userProp in $cfg.PSObject.Properties) {
    $user = $userProp.Value
    if ($null -eq $user.wallpaperconfig) { continue }
    foreach ($m in $user.wallpaperconfig.selectedwallpapers.PSObject.Properties) {
        Write-Host ("  user={0} {1} -> {2}" -f $userProp.Name, $m.Name, $m.Value.file)
    }
}

Write-Host "=== 最近记录前 10 条 ==="
foreach ($userProp in $cfg.PSObject.Properties) {
    $user = $userProp.Value
    if ($null -eq $user.wallpaperconfigrecent) { continue }
    $user.wallpaperconfigrecent | Select-Object -First 10 | ForEach-Object {
        $f = $_.config.selectedwallpapers.PSObject.Properties | Select-Object -First 1
        Write-Host ("  {0,-40} {1}" -f $_.title, $f.Value.file)
    }
    break
}
