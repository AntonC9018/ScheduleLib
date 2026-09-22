param(
    [Parameter(Mandatory = $true)]
    [string] $InputDirectory,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

try {
    foreach ($file in @(Get-ChildItem -LiteralPath $InputDirectory -Recurse -Filter '*.docx')) {
        $relative = $file.FullName.Substring($InputDirectory.TrimEnd('\').Length).TrimStart('\')
        $pdfName = [System.IO.Path]::ChangeExtension($relative.Replace('\', '__'), '.pdf')
        $pdfPath = Join-Path $OutputDirectory $pdfName
        Write-Output "Opening $pdfName"
        $document = $word.Documents.Open($file.FullName, $false, $true)
        try {
            Write-Output "Exporting $pdfPath"
            $document.ExportAsFixedFormat([string] $pdfPath, 17)
            foreach ($shape in $document.Shapes) {
                if ($shape.TextFrame.HasText -and $shape.TextFrame.Overflowing) {
                    Write-Warning "Overflowing text box in $pdfName : $($shape.TextFrame.TextRange.Text.Trim())"
                }
            }
            Write-Output "$pdfName : $($document.ComputeStatistics(2)) pages"
        }
        finally {
            $document.Close($false)
        }
    }
}
finally {
    $word.Quit()
    [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($word) | Out-Null
}
