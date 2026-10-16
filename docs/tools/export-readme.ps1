param([string]$Stamp = (Get-Date -Format 'yyyyMMdd'))

$ErrorActionPreference = 'Stop'
$docs = Split-Path $PSScriptRoot -Parent
$output = Join-Path $docs 'assets/README'
$pages = [ordered]@{
    2 = 'overview'; 3 = 'search'; 4 = 'screenshot'; 5 = 'scenario'
    6 = 'device-chat'; 7 = 'translation-compression'; 8 = 'video-compression'
    9 = 'batch-rename'; 10 = 'windows-integration'; 11 = 'quick-look'; 12 = 'plugins'
}

# PowerPoint exports slide contents directly, without capturing the desktop.
$powerPoint = New-Object -ComObject PowerPoint.Application
foreach ($language in @('zh', 'en')) {
    $path = Join-Path $docs $(if ($language -eq 'en') { 'en-us/演示文稿1.pptx' } else { '演示文稿1.pptx' })
    $deck = $powerPoint.Presentations.Open([IO.Path]::GetFullPath($path), -1, 0, 0)
    try {
        if ($deck.Slides.Count -ne 14) { throw "Expected 14 slides in $path. Update the page mapping if the deck order changed." }
        foreach ($page in $pages.GetEnumerator()) {
            $suffix = if ($language -eq 'en') { '-en' } else { '' }
            $name = "ppt-$($page.Value)$suffix-$Stamp.png"
            $deck.Slides.Item([int]$page.Key).Export((Join-Path $output $name), 'PNG', 1600, 900)
        }
    } finally { $deck.Close() }
}
