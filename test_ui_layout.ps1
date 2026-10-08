$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('MoliUiChecks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'WindowTiler') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$testExe = Join-Path $testRoot 'UiLayoutChecks.exe'
& $compiler /nologo /target:exe /main:MoliWindowTiler.UiLayoutChecks /platform:anycpu "/out:$testExe" "/win32manifest:$PSScriptRoot\WindowTiler\app.manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll /reference:System.Windows.Forms.dll $sources (Join-Path $PSScriptRoot 'Tests\UiLayoutChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI verification build failed.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw "UI layout verification failed. Artifacts: $testRoot" }
