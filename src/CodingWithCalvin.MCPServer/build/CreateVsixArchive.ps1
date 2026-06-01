param(
    [Parameter(Mandatory = $true)]
    [string] $SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string] $DestinationPath,

    [Parameter(Mandatory = $true)]
    [string] $VsixUtilPath,

    [Parameter(Mandatory = $true)]
    [string] $SourceManifestPath
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$sourceRoot = (Resolve-Path -LiteralPath $SourceDirectory).Path.TrimEnd('\', '/')
$destinationDirectory = Split-Path -Parent $DestinationPath

if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) {
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
}

if (Test-Path -LiteralPath $DestinationPath) {
    Remove-Item -LiteralPath $DestinationPath -Force
}

& $VsixUtilPath package -outputPath $DestinationPath -sourceManifest $SourceManifestPath -noValidate
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$archive = [System.IO.Compression.ZipFile]::Open($DestinationPath, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    foreach ($entryName in @('[Content_Types].xml')) {
        $existingEntry = $archive.GetEntry($entryName)
        if ($null -ne $existingEntry) {
            $existingEntry.Delete()
        }
    }

    Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | ForEach-Object {
        $relativePath = $_.FullName.Substring($sourceRoot.Length).TrimStart('\', '/')
        $entryName = $relativePath.Replace('\', '/')

        if ($entryName -eq 'extension.vsixmanifest') {
            return
        }

        $existingEntry = $archive.GetEntry($entryName)
        if ($null -ne $existingEntry) {
            $existingEntry.Delete()
        }

        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $_.FullName,
            $entryName,
            [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }
}
finally {
    $archive.Dispose()
}
