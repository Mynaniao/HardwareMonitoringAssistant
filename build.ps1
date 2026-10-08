# 硬件监控浮窗 —— 编译脚本（Windows 内置 csc.exe，无需安装 .NET SDK）
#   powershell -ExecutionPolicy Bypass -File build.ps1
# 输出: dist\HardwareMonitor.exe   （想用中文名就自己改名成 硬件监控.exe）

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { Write-Host '[x] csc.exe not found - .NET Framework 4.x is required'; exit 1 }

$outDir = Join-Path $root 'dist'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$out = Join-Path $outDir 'HardwareMonitor.exe'

$args = @(
  '/nologo'
  '/target:winexe'
  '/platform:anycpu'
  '/optimize+'
  "/out:$out"
  "/win32manifest:$(Join-Path $root 'src\app.manifest')"
  '/r:System.dll'
  '/r:System.Core.dll'
  '/r:System.Drawing.dll'
  '/r:System.Windows.Forms.dll'
  '/r:Microsoft.VisualBasic.dll'
  (Join-Path $root 'src\HardwareMonitor.cs')
)

Write-Host ("[i] compiler: " + $csc)
& $csc @args
if ($LASTEXITCODE -ne 0) { Write-Host '[x] build failed'; exit $LASTEXITCODE }

$size = (Get-Item $out).Length
Write-Host ("[ok] " + $out + "  (" + [math]::Round($size/1KB,1) + " KB)")
