# 使用 Windows 自带的 .NET Framework 编译器，不下载依赖。
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '找不到 .NET Framework 4.x C# 编译器。' }
$sourceFile = Join-Path $PSScriptRoot 'BluetoothTerminal.cs'
$outputFile = Join-Path $PSScriptRoot 'BluetoothTerminal.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/out:$outputFile" $sourceFile
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码：$LASTEXITCODE" }
Write-Host "已生成：$outputFile"
