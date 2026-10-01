param([string]$OutputZip = (Join-Path (Split-Path $PSScriptRoot -Parent) 'AutoMacro.zip'), [switch]$UpdateOnly)
$ErrorActionPreference = 'Stop'
if ($UpdateOnly -and !$PSBoundParameters.ContainsKey('OutputZip')) {
    $OutputZip = Join-Path (Split-Path $PSScriptRoot -Parent) 'AutoMacro-update.zip'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$destination = [IO.Path]::GetFullPath($OutputZip)
$staged = $destination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
# Updates contain only program files. User JSON, backups, and recovery files are never packaged.
$files = @((Get-Item -LiteralPath (Join-Path $root 'Auto Macro.exe')))
if (!$UpdateOnly) {
    foreach ($directory in @('src','tests')) {
        $files += Get-ChildItem -LiteralPath (Join-Path $root $directory) -Recurse -File -Filter '*.cs'
    }
    $files += Get-Item -LiteralPath (Join-Path $root 'assets\AutoMacro.ico')
    $files += Get-Item -LiteralPath (Join-Path $root 'build.ps1')
    $files += Get-Item -LiteralPath (Join-Path $root 'package.ps1')
}
try {
    $archive = [IO.Compression.ZipFile]::Open($staged, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            if (!$file.FullName.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'File outside package root.' }
            $entry = $file.FullName.Substring($root.Length + 1).Replace('\','/')
            if (!$UpdateOnly) { $entry = 'AutoMacro/' + $entry }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
    if (Test-Path -LiteralPath $destination) { [IO.File]::Replace($staged, $destination, [System.Management.Automation.Language.NullString]::Value) }
    else { [IO.File]::Move($staged, $destination) }
    Write-Output "Package created: $destination"
} finally { if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged } }
