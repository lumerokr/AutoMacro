param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$icon = Join-Path $PSScriptRoot 'assets\AutoMacro.ico'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Recurse -File -Filter '*.cs' | Sort-Object FullName | ForEach-Object { $_.FullName })
$sources += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -File -Filter '*.cs' | Sort-Object FullName | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/win32icon:$icon" "/resource:$icon,AutoMacro.ico" "/out:$OutputDirectory\Auto Macro.exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$test = Start-Process -FilePath "$OutputDirectory\Auto Macro.exe" -ArgumentList '--self-test' -PassThru -Wait -WindowStyle Hidden
if ($test.ExitCode -ne 0) { throw "Self-test failed: $($test.ExitCode)" }
Write-Output 'Build and self-test passed.'
