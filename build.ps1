param([string]$OutputDirectory = $PSScriptRoot, [switch]$UITests)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$icon = Join-Path $PSScriptRoot 'assets\AutoMacro.ico'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Recurse -File -Filter '*.cs' | Sort-Object FullName | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/win32icon:$icon" "/resource:$icon,AutoMacro.ico" "/out:$OutputDirectory\Auto Macro.exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('AutoMacro-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDirectory | Out-Null
try {
    $appExecutable = [IO.Path]::GetFullPath((Join-Path $OutputDirectory 'Auto Macro.exe'))
    Copy-Item -LiteralPath $appExecutable -Destination (Join-Path $testDirectory 'Auto Macro.exe')
    $testSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -File -Filter '*.cs' | Sort-Object FullName | ForEach-Object { $_.FullName })
    $testExecutable = Join-Path $testDirectory 'AutoMacro.Tests.exe'
    & $compiler /nologo /target:exe /platform:anycpu /main:AutoMacro.TestProgram /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/reference:$appExecutable" "/out:$testExecutable" @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    $checks = @('--self-test'); if ($UITests) { $checks += '--ui-test' }
    foreach ($check in $checks) {
        $test = Start-Process -FilePath $testExecutable -ArgumentList $check -PassThru -Wait -WindowStyle Hidden -RedirectStandardError (Join-Path $testDirectory 'test-errors.log')
        if ($test.ExitCode -ne 0) { throw "$check failed ($($test.ExitCode)): $(Get-Content -LiteralPath (Join-Path $testDirectory 'test-errors.log') -Raw)" }
        Write-Output "$check passed."
    }
    Write-Output 'Build passed. Tests are separate from the distributed executable.'
} finally {
    foreach ($file in @('AutoMacro.Tests.exe','Auto Macro.exe','test-errors.log')) {
        $testFile = Join-Path $testDirectory $file
        if (Test-Path -LiteralPath $testFile) { [IO.File]::Delete($testFile) }
    }
    if (@(Get-ChildItem -LiteralPath $testDirectory -Force).Count -eq 0) { [IO.Directory]::Delete($testDirectory) }
}
