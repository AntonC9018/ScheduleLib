[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $NewDataDirectory,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,

    [string] $LegacyTemplateDirectory,

    [string] $DocumentFilter = '*'
)

$ErrorActionPreference = 'Stop'

$wdCollapseStart = 1
$wdRelativeHorizontalPositionPage = 1
$wdRelativeVerticalPositionPage = 1
$wdWrapNone = 3
$wdPaperA4 = 7
$msoTextOrientationHorizontal = 1
$msoTrue = -1
$msoFalse = 0
$msoBringToFront = 0

function Get-NthRange {
    param(
        $Document,
        [string] $Text,
        [int] $Occurrence = 1
    )

    $found = 0
    # Main-story paragraph ranges provide actual Word positions, including
    # table boundaries. Content.Text offsets do not include all such positions.
    foreach ($paragraph in $Document.Content.Paragraphs) {
        $scope = $paragraph.Range
        $sourceText = $scope.Text
        $start = 0
        $occurrenceInParagraph = 0
        while ($start -lt $sourceText.Length) {
            $offset = $sourceText.IndexOf($Text, $start, [StringComparison]::Ordinal)
            if ($offset -lt 0) { break }
            $found++
            $occurrenceInParagraph++
            if ($found -eq $Occurrence) {
                return Find-InRange $Document $scope $Text $occurrenceInParagraph
            }
            $start = $offset + $Text.Length
        }
    }
    throw "Text occurrence $Occurrence not found: $Text"
}

function Get-RangeAfterText {
    param(
        $Document,
        [string] $Text,
        [int] $Occurrence = 1
    )

    $range = Get-NthRange $Document $Text $Occurrence
    $range.Collapse(0)
    return $range
}

function Get-RangeWithinText {
    param(
        $Document,
        [string] $ContainerText,
        [string] $InnerText,
        [int] $ContainerOccurrence = 1,
        [int] $InnerOccurrence = 1
    )

    $container = Get-NthRange $Document $ContainerText $ContainerOccurrence
    if ($ContainerText -ceq $InnerText -and $InnerOccurrence -eq 1) { return $container }
    return Find-InRange $Document $container $InnerText $InnerOccurrence
}

function Find-InRange {
    param($Document, $Scope, [string] $Text, [int] $Occurrence = 1)
    $scopeStart = $Scope.Start
    $scopeEnd = $Scope.End
    $search = $Scope.Duplicate
    for ($index = 1; $index -le $Occurrence; $index++) {
        $finder = $search.Find
        $finder.ClearFormatting()
        if (-not $finder.Execute($Text, $true, $false, $false, $false, $false, $true, 0, $false)) {
            throw "Text occurrence $Occurrence not found inside selected range: $Text"
        }
        $match = $finder.Parent
        if ($match.Text -cne $Text) { throw "Word returned a different match: $Text" }
        if ($match.Start -lt $scopeStart -or $match.End -gt $scopeEnd) {
            throw "Word search escaped its selected range: $Text"
        }
        $search = $Document.Range($match.End, $scopeEnd)
    }
    return $match
}

function Get-RangeAfterLabelInParagraph {
    param(
        $Document,
        [string] $Label,
        [string] $Text,
        [int] $LabelOccurrence = 1,
        [int] $TextOccurrence = 1
    )

    $labelRange = Get-NthRange $Document $Label $LabelOccurrence
    $scope = $Document.Range($labelRange.End, $labelRange.Paragraphs.Item(1).Range.End)
    return Find-InRange $Document $scope $Text $TextOccurrence
}

function Add-Overlay {
    param(
        $Document,
        $TargetRange,
        [string] $Token,
        [float] $Width,
        [float] $Height = 14,
        [float] $LeftAdjust = 0,
        [float] $TopAdjust = -1,
        [float] $FontSize = 11,
        [string] $Alignment = 'left'
    )

    $anchor = $TargetRange.Duplicate
    $anchor.Collapse($wdCollapseStart)
    $Height = [Math]::Max($Height, $FontSize * 1.2 + 0.6)
    # Range.Information omits indentation/alignment offsets in Word. Selecting
    # the insertion point gives the actual printed page coordinates.
    $anchor.Select()
    $left = [float] $Document.Application.Selection.Information(5) + $LeftAdjust
    $top = [float] $Document.Application.Selection.Information(6) + $TopAdjust
    if ($TargetRange.Text -match '^_+$') {
        $end = $TargetRange.Duplicate
        $end.Collapse(0)
        $end.Select()
        if ([Math]::Abs([float] $Document.Application.Selection.Information(6) - ($top - $TopAdjust)) -lt 1) {
            $blankWidth = [float] $Document.Application.Selection.Information(5) - ($left - $LeftAdjust)
            if ($blankWidth -gt 0) { $Width = [Math]::Min($Width, $blankWidth - $LeftAdjust) }
        }
    }
    if ($TargetRange.Information(12)) {
        $cell = $TargetRange.Cells.Item(1)
        $cellStart = $cell.Range.Duplicate
        $cellStart.Collapse($wdCollapseStart)
        $cellStart.Select()
        $cellRight = [float] $Document.Application.Selection.Information(5) + $cell.Width - $cell.LeftPadding - $cell.RightPadding
        $Width = [Math]::Min($Width, $cellRight - $left)
    }
    Write-Verbose "$Token at $left,$top size $Width,$Height target '$($TargetRange.Text)' start $($TargetRange.Start)"
    if ($left -lt 0 -or $top -lt 0 -or $Width -le 0) {
        throw "Word could not determine the page position for $Token"
    }

    $shape = $Document.Shapes.AddTextbox(
        $msoTextOrientationHorizontal,
        $left,
        $top,
        $Width,
        $Height,
        $anchor)
    $shape.RelativeHorizontalPosition = $wdRelativeHorizontalPositionPage
    $shape.RelativeVerticalPosition = $wdRelativeVerticalPositionPage
    $shape.WrapFormat.Type = $wdWrapNone
    $shape.LayoutInCell = $msoFalse
    $shape.Left = $left
    $shape.Top = $top
    $shape.LockAnchor = $msoTrue
    $shape.Fill.Visible = $msoTrue
    $shape.Fill.ForeColor.RGB = 16777215
    $shape.Fill.Transparency = 0
    $shape.Line.Visible = $msoFalse
    $shape.TextFrame.MarginLeft = 0
    $shape.TextFrame.MarginRight = 0
    $shape.TextFrame.MarginTop = 0
    $shape.TextFrame.MarginBottom = 0
    $shape.TextFrame.AutoSize = $msoFalse
    $shape.TextFrame.TextRange.Text = "{{$Token}}"
    $shape.TextFrame.TextRange.Font.Name = 'Times New Roman'
    $shape.TextFrame.TextRange.Font.Size = $FontSize
    $shape.TextFrame.TextRange.ParagraphFormat.SpaceBefore = 0
    $shape.TextFrame.TextRange.ParagraphFormat.SpaceAfter = 0
    $shape.TextFrame.TextRange.ParagraphFormat.LineSpacingRule = 0
    $shape.TextFrame.TextRange.ParagraphFormat.Alignment = switch ($Alignment) {
        'center' { 1 }
        'right' { 2 }
        default { 0 }
    }
    $shape.ZOrder($msoBringToFront)
}

function Add-OverlayOnText {
    param(
        $Document,
        [string] $Text,
        [string] $Token,
        [int] $Occurrence = 1,
        [float] $Width = 0,
        [float] $Height = 14,
        [float] $LeftAdjust = 0,
        [float] $TopAdjust = -1,
        [float] $FontSize = 11,
        [string] $Alignment = 'left'
    )

    $range = Get-NthRange $Document $Text $Occurrence
    if ($Width -le 0) {
        $end = $range.Duplicate
        $end.Collapse(0)
        $measuredWidth = [float] $end.Information(5) - [float] $range.Information(5)
        $Width = [Math]::Max(12, $measuredWidth)
    }
    Add-Overlay $Document $range $Token $Width $Height $LeftAdjust $TopAdjust $FontSize $Alignment
}

function Add-OverlayAfterText {
    param(
        $Document,
        [string] $Text,
        [string] $Token,
        [int] $Occurrence = 1,
        [float] $Width,
        [float] $Height = 14,
        [float] $LeftAdjust = 2,
        [float] $TopAdjust = -1,
        [float] $FontSize = 11,
        [string] $Alignment = 'left'
    )

    $range = Get-RangeAfterText $Document $Text $Occurrence
    Add-Overlay $Document $range $Token $Width $Height $LeftAdjust $TopAdjust $FontSize $Alignment
}

function Add-OverlayAfterLabel {
    param(
        $Document,
        [string] $Label,
        [string] $Text,
        [string] $Token,
        [int] $LabelOccurrence = 1,
        [int] $TextOccurrence = 1,
        [float] $Width = 0,
        [float] $Height = 14,
        [float] $LeftAdjust = 0,
        [float] $TopAdjust = -1,
        [float] $FontSize = 11,
        [string] $Alignment = 'left'
    )

    $range = Get-RangeAfterLabelInParagraph $Document $Label $Text $LabelOccurrence $TextOccurrence
    if ($Width -le 0) {
        $end = $range.Duplicate
        $end.Collapse(0)
        $Width = [Math]::Max(12, [float] $end.Information(5) - [float] $range.Information(5))
    }
    Add-Overlay $Document $range $Token $Width $Height $LeftAdjust $TopAdjust $FontSize $Alignment
}

function Add-OverlayWithinText {
    param(
        $Document,
        [string] $ContainerText,
        [string] $InnerText,
        [string] $Token,
        [int] $ContainerOccurrence = 1,
        [int] $InnerOccurrence = 1,
        [float] $Width = 0,
        [float] $Height = 14,
        [float] $LeftAdjust = 0,
        [float] $TopAdjust = -1,
        [float] $FontSize = 11,
        [string] $Alignment = 'left'
    )

    $range = Get-RangeWithinText $Document $ContainerText $InnerText $ContainerOccurrence $InnerOccurrence
    if ($Width -le 0) {
        $end = $range.Duplicate
        $end.Collapse(0)
        $Width = [Math]::Max(12, [float] $end.Information(5) - [float] $range.Information(5))
    }
    Add-Overlay $Document $range $Token $Width $Height $LeftAdjust $TopAdjust $FontSize $Alignment
}

function Add-OverlayToCell {
    param(
        $Document,
        [int] $TableIndex,
        [int] $RowIndex,
        [int] $ColumnIndex,
        [string] $Token,
        [float] $FontSize = 11
    )

    $target = $Document.Tables.Item($TableIndex).Cell($RowIndex, $ColumnIndex).Range.Duplicate
    $target.End = $target.Start
    Add-Overlay $Document $target $Token 125 14 4 2 $FontSize 'center'
}

function Add-OverlayAfterCellLabel {
    param($Document, [int] $TableIndex, [int] $RowIndex, [int] $ColumnIndex,
          [string] $Label, [string] $Token, [float] $Width,
          [float] $Height = 13, [float] $FontSize = 10)
    $scope = $Document.Tables.Item($TableIndex).Cell($RowIndex, $ColumnIndex).Range
    $range = Find-InRange $Document $scope $Label
    $range.Collapse(0)
    Add-Overlay $Document $range $Token $Width $Height 3 0 $FontSize
}

function Prepare-Document {
    param(
        $Word,
        [string] $SourceName,
        [string] $OutputName,
        [scriptblock] $Prepare,
        [string] $SourceDirectory = $NewDataDirectory
    )

    if ($OutputName -notlike $DocumentFilter) { return }
    $source = Join-Path $SourceDirectory $SourceName
    $target = Join-Path $OutputDirectory $OutputName
    Copy-Item -LiteralPath $source -Destination $target -Force
    $document = $Word.Documents.Open($target, $false, $false)
    try {
        $document.Repaginate()
        & $Prepare $document
        $document.Save()
        Write-Output "Prepared $OutputName"
    }
    finally {
        $document.Close($false)
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0

try {
    Prepare-Document $word '3. Cerere angajare_didactica.docx' 'cerere_angajare_didactica.docx' {
        param($document)
        Add-OverlayAfterText $document 'Subsemnatul(a)' 'Name' 1 350 15 4 -1 12
        Add-OverlayAfterLabel $document 'în funcția de ' '__________________' 'Function' 1 1 132 14 0 -1 12 'center'
        Add-OverlayAfterLabel $document 'în cadrul Facultății ' '________________________________________________' 'Faculty' 1 1 344 14 0 -1 12 'center'
        Add-OverlayAfterLabel $document 'la Departamentul ' '____________________________________' 'Department' 1 1 260 14 0 -1 12 'center'
        Add-OverlayOnText $document '2026–2027' 'AcademicYear' 1 54 14 0 -1 11.5 'center'
        Add-OverlayAfterLabel $document 'în regim de ' '_______________________________________________' 'HireType' 1 1 340 14 0 -1 12 'center'
        Add-OverlayAfterLabel $document 'cu o normă didactică de ' '____________________________________________' 'Units' 1 1 320 14 0 -1 12 'center'
        Add-OverlayOnText $document '01.09.2026' 'DateFrom' 1 54 14 0 -1 11.5 'center'
        Add-OverlayOnText $document 'xx.xx.2027' 'DateTo' 1 54 14 0 -1 11.5 'center'
        Add-OverlayAfterText $document 'Data' 'DocumentDate' 1 86 14 8 -1 12 'center'
    }

    Prepare-Document $word '10. fișa_Asistent universitar actualizat 2026.docx' 'fisa_postului_asistent_universitar.docx' {
        param($document)
        Add-OverlayAfterLabel $document 'Subdiviziunea structurală: Facultatea ' '_______________________________________________' 'Faculty' 1 1 325 14 0 -1 11
        Add-OverlayAfterLabel $document 'Departamentul ' '____________________________________________________________________' 'Department' 1 1 470 14 0 -1 11
        Add-OverlayAfterLabel $document 'Nume, prenume ' '__________________________________________________' 'PreparedByName' 1 1 345 14 0 -1 11
        Add-OverlayAfterLabel $document 'Șef Departament' '__________________________________________________' 'PreparedByDepartment' 1 1 345 14 3 -1 11
        Add-OverlayAfterLabel $document 'Data ' '_______________________________' 'DocumentDate' 1 1 214 14 0 -1 11 'center'
        Add-OverlayAfterLabel $document 'Nume, prenume ' '______________________________________________________' 'Name' 2 1 380 14 0 -1 11
        Add-OverlayAfterLabel $document 'Data ' '______________________' 'DocumentDate' 3 1 150 14 0 -1 11 'center'
    }

    Prepare-Document $word '7. DECLARAȚIE DE CONSIMȚĂMÂNT.docx' 'declaratie_consimtamant.docx' {
        param($document)
        Add-OverlayAfterLabel $document 'Subsemnatul/Subsemnata, ' '__________________________________________________' 'Name' 1 1 350 14 0 -1 11 'center'
        Add-OverlayAfterLabel $document 'IDNP ' '________________________________' 'PersonalIdentifier' 1 1 220 14 0 -1 11 'center'
        Add-OverlayOnText $document '_______________________________________________________' 'Name' 1 380 14 0 -1 11 'center'
        Add-OverlayWithinText $document '„____” __________________ 20______' '„____” __________________' 'DocumentDayMonth' 1 1 143 14 0 0 12 'center'
        Add-OverlayWithinText $document '20______' '______' 'DocumentYear2' 1 1 42 14 0 0 12 'left'
    }

    Prepare-Document $word '8. DECLARAȚIE DE INFORMARE.docx' 'declaratie_informare.docx' {
        param($document)
        Add-OverlayAfterLabel $document 'Subsemnatul/Subsemnata, ' '___________________________________________________' 'Name' 1 1 355 14 0 -1 11 'center'
        Add-OverlayAfterLabel $document 'IDNP ' '________________________________' 'PersonalIdentifier' 1 1 220 14 0 -1 11 'center'
        Add-OverlayOnText $document '_______________________________________________________' 'Name' 1 380 14 0 -1 11 'center'
        Add-OverlayWithinText $document '„____” __________________ 20______' '„____” __________________' 'DocumentDayMonth' 1 1 143 14 0 0 12 'center'
        Add-OverlayWithinText $document '20______' '______' 'DocumentYear2' 1 1 42 14 0 0 12 'left'
    }

    Prepare-Document $word '5. CIM didactica 2026 red.3.docx' 'contract_individual_de_munca.docx' {
        param($document)
        $document.PageSetup.PaperSize = $wdPaperA4
        Add-OverlayAfterLabel $document 'din ' '_______________________20____' 'DocumentDate' 1 1 145 14 0 -1 11 'center'
        Add-OverlayAfterLabel $document 'și Dl/Dna ' '___________________________________________________________________________' 'Name' 1 1 480 14 0 -1 11 'center'
        Add-OverlayAfterLabel $document 'domiciliat(ă) în ' '_____________________________________________' 'HomeAddress' 1 1 285 14 0 -1 8 'center'
        Add-OverlayAfterLabel $document 'IDNP ' '___________________________________' 'PersonalIdentifier' 1 1 230 14 0 -1 10 'center'
        Add-OverlayAfterLabel $document 'funcția didactică/științifico-didactică de ' '___________________________________________' 'Function' 1 1 290 14 0 -1 11 'center'
        Add-OverlayAfterLabel $document 'Locul de muncă este ' '__________________________' 'Workplace' 1 1 180 14 0 -1 10 'center'
        # This blank starts its own otherwise empty line; use that horizontal
        # space for the full street address and its terminating full stop.
        Add-OverlayAfterText $document 'str.   ' 'WorkplaceAddressCompact' 1 260 13 0 -1 9
        Add-OverlayAfterLabel $document 'Munca se prestează pe ' '______' 'Units' 1 1 42 14 0 -1 11 'center'
        Add-OverlayAfterLabel $document 'caracter de: ' '__________________________________' 'CimHireType' 1 1 230 14 0 -1 11 'center'
        Add-OverlayOnText $document 'În cazul cumulului extern, Salariatul declară că deține funcția de bază de____________________________________ la ' 'ExternalEmploymentClause' 1 500 72 0 -1 10
        Add-OverlayOnText $document '_____________________până la ____________________________________' 'ContractPeriod' 1 350 14 0 -1 11 'center'
        Add-OverlayToCell $document 1 1 2 'OccupationCode' 11
        Add-OverlayAfterLabel $document 'Nume, prenume: ' '_________________________________________' 'Name' 1 1 285 14 0 -1 10
        Add-OverlayAfterLabel $document 'Adresa: ' '______________________________________________' 'HomeAddress' 2 1 315 14 0 -1 7.5
        Add-OverlayAfterLabel $document 'Telefon: ' '________________________________________________' 'PhoneNumber' 2 1 325 14 0 -1 10
        Add-OverlayAfterLabel $document 'Act de identitate: ' '__________________' 'BISeries' 1 1 125 14 0 -1 10
        Add-OverlayAfterLabel $document 'Data eliberării ' '__________' 'IDIssueDate' 1 1 70 14 0 -1 9
        Add-OverlayAfterLabel $document 'IDNP: ' '__________________________________________________' 'PersonalIdentifier' 1 1 340 14 0 -1 10
        Add-OverlayAfterLabel $document 'E-mail: ' '_________________________________________________' 'Email' 1 1 335 14 0 -1 9
    }

    Prepare-Document $word '1. acord suplimentar  de modificare 2026.docx' 'acord_suplimentar.docx' {
        param($document)
        Add-OverlayWithinText $document "ACORD SUPLIMENTAR nr.`t__din`t20 `t_" 'din' 'DocumentDate' 1 1 105 13 20 -1 10 'center'
        Add-OverlayWithinText $document "cu privire la modificarea contractului individual de muncă nr.`tdin  `t" 'din  ' 'DocumentDate' 1 1 149 13 17 -1 10 'center'
        Add-OverlayWithinText $document '„' '„' 'DocumentDate' 1 1 178 13 0 -1 10
        Add-OverlayAfterText $document 'și salariatul' 'Name' 1 210 13 4 -1 10
        Add-OverlayAfterLabel $document 'IDNP  ' '____________________' 'PersonalIdentifier' 1 1 140 13 0 -1 10 'center'
        Add-OverlayAfterLabel $document 'intră în vigoare din  ' '__________' 'DocumentDate' 1 1 72 13 0 -1 10 'center'
        Add-OverlayToCell $document 1 1 2 'Units' 10
        Add-OverlayAfterCellLabel $document 2 1 2 'Salariatul' 'Name' 235 13 10
        Add-OverlayAfterCellLabel $document 2 2 2 'Adresa' 'HomeAddress' 250 26 9
        Add-OverlayAfterCellLabel $document 2 4 2 'mobil' 'PhoneNumber' 130 13 10
        Add-OverlayAfterCellLabel $document 2 5 2 'Buletin de identitate' 'BISeries' 70 13 9
        Add-OverlayAfterCellLabel $document 2 5 2 'data eliberării' 'IDIssueDate' 52 13 9
        Add-OverlayAfterCellLabel $document 2 6 2 'Cod personal' 'PersonalIdentifier' 150 13 9
        Add-OverlayAfterCellLabel $document 2 7 2 'Adresa e-mail' 'Email' 240 13 9

        Add-OverlayWithinText $document "ACORD SUPLIMENTAR nr.`t__din`t20 `t_" 'din' 'DocumentDate' 2 1 105 13 20 -1 10 'center'
        Add-OverlayWithinText $document "cu privire la modificarea contractului individual de muncă nr.`tdin  `t" 'din  ' 'DocumentDate' 2 1 149 13 17 -1 10 'center'
        Add-OverlayWithinText $document '„' '„' 'DocumentDate' 2 1 178 13 0 -1 10
        Add-OverlayAfterText $document 'și salariatul' 'Name' 2 210 13 4 -1 10
        Add-OverlayAfterLabel $document 'IDNP  ' '____________________' 'PersonalIdentifier' 2 1 140 13 0 -1 10 'center'
        Add-OverlayAfterLabel $document 'intră în vigoare din  ' '__________' 'DocumentDate' 2 1 72 13 0 -1 10 'center'
        Add-OverlayToCell $document 3 1 2 'Units' 10
        Add-OverlayAfterCellLabel $document 4 1 2 'Salariatul' 'Name' 235 13 10
        Add-OverlayAfterCellLabel $document 4 2 2 'Adresa' 'HomeAddress' 250 26 9
        Add-OverlayAfterCellLabel $document 4 4 2 'mobil' 'PhoneNumber' 130 13 10
        Add-OverlayAfterCellLabel $document 4 5 2 'Buletin de identitate' 'BISeries' 70 13 9
        Add-OverlayAfterCellLabel $document 4 5 2 'data eliberării' 'IDIssueDate' 52 13 9
        Add-OverlayAfterCellLabel $document 4 6 2 'Cod personal' 'PersonalIdentifier' 150 13 9
        Add-OverlayAfterCellLabel $document 4 7 2 'Adresa e-mail' 'Email' 240 13 9
    }
    if ($LegacyTemplateDirectory) {
        Prepare-Document $word 'declaratie_proprie_raspundere.docx' 'declaratie_proprie_raspundere.docx' {
            param($document)
            $fields = @('Name', 'Function', 'Department', 'Faculty', 'Date')
            # The PDF's blank lines are represented by bordered cells in this
            # editable source. Remove only legacy expressions, retaining cells.
            for ($index = 0; $index -lt $fields.Count; $index++) {
                $range = Get-NthRange $document ('<#<Content Select="./' + $fields[$index] + '"/>#>')
                $range.Text = ''
            }
            $document.Repaginate()
            for ($index = 0; $index -lt $fields.Count; $index++) {
                $cell = $document.Tables.Item($index + 1).Cell(1, 2)
                $range = $cell.Range.Duplicate
                $range.Collapse($wdCollapseStart)
                $token = if ($fields[$index] -eq 'Date') { 'DocumentDate' } else { $fields[$index] }
                $width = $cell.Width - $cell.LeftPadding - $cell.RightPadding
                # The caret in an empty centered cell is at its midpoint, not
                # at the start of the original underlined blank.
                $leftAdjust = switch ($range.ParagraphFormat.Alignment) {
                    1 { -$width / 2 }
                    2 { -$width }
                    default { 0 }
                }
                Add-Overlay $document $range $token $width 14 $leftAdjust -1 11 'center'
            }
        } $LegacyTemplateDirectory
    }
}
finally {
    $word.Quit()
    [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($word) | Out-Null
}
