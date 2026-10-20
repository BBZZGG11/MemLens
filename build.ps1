#requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$NoClean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$exe = Join-Path $dist 'MemLens.exe'

$candidates = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
)
$csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw '找不到 .NET Framework 4.x 的 csc.exe。请先启用 .NET Framework 4.8。' }

New-Item -ItemType Directory -Force -Path $dist | Out-Null
if (-not $NoClean) {
    Get-ChildItem -Path $dist -File -ErrorAction SilentlyContinue | Remove-Item -Force
}

$sources = Get-ChildItem -Path $srcDir -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName }
if ($sources.Count -lt 5) { throw 'src 目录中的 C# 源文件不完整。' }

$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/langversion:5',
    '/codepage:65001',
    "/win32manifest:$root\app.manifest",
    "/win32icon:$root\MemLens.ico",
    "/out:$exe",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll'
) + $sources

Write-Host 'Compiling MemLens.exe ...' -ForegroundColor Cyan
& $csc $cscArgs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) {
    throw "编译失败，csc 退出码 $LASTEXITCODE"
}

Copy-Item -Path (Join-Path $root 'Get-MemoryDiagnosis.ps1') -Destination $dist -Force
Copy-Item -Path (Join-Path $root 'README.md') -Destination $dist -Force
Copy-Item -Path (Join-Path $root 'USAGE.txt') -Destination $dist -Force
Copy-Item -Path (Join-Path $root 'screenshot.png') -Destination $dist -Force
Copy-Item -Path (Join-Path $root 'app.config') -Destination (Join-Path $dist 'MemLens.exe.config') -Force

$item = Get-Item $exe
$hash = Get-FileHash -Path $exe -Algorithm SHA256
Write-Host ''
Write-Host 'Build OK' -ForegroundColor Green
Write-Host ('  EXE : ' + $item.FullName)
Write-Host ('  Size: ' + [math]::Round($item.Length / 1KB, 1) + ' KB')
Write-Host ('  SHA256: ' + $hash.Hash)
Write-Host ('  Dist: ' + $dist)
Write-Host ''
Write-Host 'Self-test:' -ForegroundColor Cyan
Write-Host ('  Start-Process -FilePath "' + $exe + '" -ArgumentList "--selftest","' + (Join-Path $dist 'selftest.txt') + '" -Wait')
