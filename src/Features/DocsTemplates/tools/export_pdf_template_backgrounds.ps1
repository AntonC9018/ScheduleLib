[CmdletBinding()]
param(
    [string] $TemplateDirectory = (Join-Path $PSScriptRoot '..\data\templates'),
    [string] $LayoutDirectory = (Join-Path $PSScriptRoot '..\data\templates'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\tmp\pdfs\backgrounds')
)

$ErrorActionPreference = 'Stop'
$templateRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($TemplateDirectory)
$layoutRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LayoutDirectory)
$outputRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("DocsTemplates-Pdf-" + [Guid]::NewGuid().ToString('N'))

New-Item -ItemType Directory -Path $outputRoot, $temporaryRoot -Force | Out-Null

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0

try {
    foreach ($layoutFile in Get-ChildItem -LiteralPath $layoutRoot -Filter '*.json' | Sort-Object Name) {
        $layout = Get-Content -Raw -LiteralPath $layoutFile.FullName | ConvertFrom-Json
        $sourceName = [IO.Path]::ChangeExtension([string] $layout.template, '.docx')
        $sourcePath = Join-Path $templateRoot $sourceName
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Template not found: $sourcePath"
        }

        $workingPath = Join-Path $temporaryRoot $sourceName
        Copy-Item -LiteralPath $sourcePath -Destination $workingPath -Force

        $document = $null
        try {
            $document = $word.Documents.Open($workingPath, $false, $false)
            if ($null -eq $document) {
                throw "Microsoft Word could not open $workingPath"
            }

            $removed = 0
            for ($index = $document.Shapes.Count; $index -ge 1; $index--) {
                $shape = $document.Shapes.Item($index)
                if ($shape.Type -ne 17 -or $shape.TextFrame.HasText -ne -1) {
                    continue
                }

                $text = ([string] $shape.TextFrame.TextRange.Text).Trim([char] 13, [char] 7, [char] 32)
                if ($text -match '^\{\{[^{}]+\}\}$') {
                    $shape.Delete()
                    $removed++
                }
            }

            if ($removed -eq 0) {
                throw "No generated placeholder text boxes were found in $sourceName"
            }

            $document.Repaginate()
            $pdfPath = Join-Path $outputRoot ([string] $layout.template)
            $document.ExportAsFixedFormat(
                $pdfPath,
                17,
                $false,
                0,
                0,
                1,
                999,
                0,
                $true,
                $true,
                1,
                $true,
                $true,
                $false)

            Write-Output "Exported $sourceName after removing $removed generated text boxes."
        }
        finally {
            if ($null -ne $document) {
                $document.Close(0)
                [Runtime.InteropServices.Marshal]::FinalReleaseComObject($document) | Out-Null
            }
        }
    }
}
finally {
    $word.Quit()
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($word) | Out-Null
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()

    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTemporaryRoot.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
