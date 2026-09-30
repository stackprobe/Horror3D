# Release build and package (Windows x64, including the .NET runtime).
# Temporary files are created only in a unique directory under C:\temp.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$stage = $null

try {
    $solution = Join-Path $root 'src\Horror.sln'
    $docs = Join-Path $root 'doc'
    if (!(Test-Path -LiteralPath $solution -PathType Leaf)) {
        throw 'src\Horror.sln was not found.'
    }
    if (!(Test-Path -LiteralPath $docs -PathType Container)) {
        throw 'doc was not found.'
    }
    Get-Command dotnet -ErrorAction Stop | Out-Null

    $stage = Join-Path 'C:\temp' ('Horror3D-release-' + [Guid]::NewGuid().ToString('N'))
    $payload = Join-Path $stage 'payload'
    New-Item -ItemType Directory -Path $payload | Out-Null

    & dotnet publish $solution --configuration Release --runtime win-x64 --self-contained true ('-p:PublishDir=' + $payload + '/') -p:UseAppHost=true
    if ($LASTEXITCODE -ne 0) {
        throw ('Release build failed (exit code ' + $LASTEXITCODE + ').')
    }
    if (!(Test-Path -LiteralPath (Join-Path $payload 'HorrorGame.exe') -PathType Leaf)) {
        throw 'HorrorGame.exe was not generated.'
    }

    Copy-Item -LiteralPath $docs -Destination (Join-Path $payload 'doc') -Recurse -Force
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = Join-Path $stage 'Horror3D.zip'
    [System.IO.Compression.ZipFile]::CreateFromDirectory($payload, $archive, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $out = Join-Path $root 'out'
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    $destination = Join-Path $out 'Horror3D.zip'
    Move-Item -LiteralPath $archive -Destination $destination -Force
    Write-Host ('Created: ' + $destination)
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
} finally {
    if ($stage -and (Test-Path -LiteralPath $stage)) {
        $resolved = [System.IO.Path]::GetFullPath($stage)
        if ($resolved -match '^C:\\temp\\Horror3D-release-[0-9a-f]{32}$') {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
