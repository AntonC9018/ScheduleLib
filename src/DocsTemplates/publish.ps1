# publish.ps1

param (
    [string]$ProjectPath = ".",
    [string]$OutputPath = "publish",
    [string]$Runtime = "win-x64"
)

# Resolve paths
$ProjectFullPath = Resolve-Path $ProjectPath
$PublishDir = Join-Path $ProjectFullPath $OutputPath

# Clean old publish
if (Test-Path $PublishDir) {
    Remove-Item -Recurse -Force $PublishDir
}

# Publish
dotnet publish $ProjectFullPath `
    -c Release `
    -r $Runtime `
    --self-contained `
    -p:PublishSingleFile=true `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed."
    exit $LASTEXITCODE
}

# Archive
$ArchivePath = "$PublishDir.zip"
if (Test-Path $ArchivePath) {
    Remove-Item -Force $ArchivePath
}
Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $ArchivePath

Write-Host "Published and archived at: $ArchivePath"

# Delete unzipped publish folder
if (Test-Path $PublishDir) {
    Remove-Item -Recurse -Force $PublishDir
    Write-Host "Removed unzipped publish folder: $PublishDir"
}

# Open the folder containing the archive in Explorer
Invoke-Item (Split-Path $ArchivePath)
