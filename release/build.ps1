# 编译 WpeVideoLauncher.exe (不需要 Visual Studio, 用系统自带 .NET Framework 编译器)
# 用法: powershell -ExecutionPolicy Bypass -File build.ps1
[CmdletBinding()]
param(
    [switch]$NoIcon
)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$releaseRoot = Split-Path -Parent $MyInvocation.MyCommand.Path       # release\
$src  = Join-Path (Split-Path -Parent $releaseRoot) 'src'            # src\
$tools = Join-Path (Split-Path -Parent $releaseRoot) 'tools'         # tools\
$out  = Join-Path $releaseRoot 'WpeVideoLauncher.exe'
$ico  = Join-Path $src 'app.ico'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '找不到 csc.exe (.NET Framework 4.x)，请确认系统已启用 .NET Framework 4.8' }

Write-Host "编译器: $csc"

# 图标：没有就先生成
if (-not $NoIcon -and -not (Test-Path $ico)) {
    $mk = Join-Path $tools 'make-icon.ps1'
    if (Test-Path $mk) {
        Write-Host "生成图标..."
        & powershell -NoProfile -ExecutionPolicy Bypass -File $mk | Out-Null
    }
}

$code = Join-Path $src 'WpeVideoLauncher.cs'
$manifest = Join-Path $src 'app.manifest'

$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/langversion:5'
    "/out:$out"
    "/win32manifest:$manifest"
    '/reference:System.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
    '/reference:System.Core.dll'
)
if (-not $NoIcon -and (Test-Path $ico)) { $cscArgs += "/win32icon:$ico" }
$cscArgs += $code

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败 (exit $LASTEXITCODE)" }

$fi = Get-Item $out
Write-Host ("编译成功: {0} ({1:N0} 字节)" -f $fi.FullName, $fi.Length)
