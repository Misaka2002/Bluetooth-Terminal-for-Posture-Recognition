# 使用 Windows 自带的 .NET Framework 编译器，无需下载依赖。
param([switch]$Test)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '找不到 .NET Framework 4.x C# 编译器。' }
$sourceDirectory = Join-Path $PSScriptRoot 'src'
$common = @('/nologo', '/platform:anycpu', '/optimize+', '/codepage:65001', '/reference:System.dll', '/reference:System.Core.dll')
if ($Test) {
    $testDirectory = Join-Path $PSScriptRoot '.build\test-results'
    New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
    $testExe = Join-Path $testDirectory 'CoreTests.exe'
    & $compiler @common /target:exe "/out:$testExe" (Join-Path $sourceDirectory 'PostureCore.cs') (Join-Path $PSScriptRoot 'tests\CoreTests.cs')
    if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
    & $testExe (Join-Path $testDirectory 'export')
    if ($LASTEXITCODE -ne 0) { throw '计时核心测试失败。' }
} else {
    $outputFile = Join-Path $PSScriptRoot 'PostureStatisticsTerminal.exe'
    $sources = @('PostureCore.cs', 'TerminalForm.cs', 'ModernControls.cs', 'Program.cs') | ForEach-Object { Join-Path $sourceDirectory $_ }
    $atlas = Join-Path $PSScriptRoot 'assets\posture-atlas.png'
    if (-not (Test-Path -LiteralPath $atlas)) { throw '缺少 3D 姿态图集。' }
    $manifest = Join-Path $sourceDirectory 'app.manifest'
    $options = @('/target:winexe', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', "/out:$outputFile", "/resource:$atlas,PostureStatistics.PostureAtlas.png", "/win32manifest:$manifest")
    & $compiler @common @options @sources
    if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码：$LASTEXITCODE" }
    Write-Host "已生成：$outputFile"
}
