[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$errors = [System.Collections.Generic.List[string]]::new()
$projectFiles = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Recurse -Filter '*.csproj' -File)

function Get-RelativePath {
    param([string] $BasePath, [string] $TargetPath)
    $baseUri = [Uri]::new($BasePath.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar)
    $targetUri = [Uri]::new($TargetPath)
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
}

foreach ($projectFile in $projectFiles) {
    [xml] $project = Get-Content -LiteralPath $projectFile.FullName -Raw
    foreach ($reference in $project.SelectNodes('//ProjectReference')) {
        $include = $reference.GetAttribute('Include')
        if ($include -match '\$\(GmaModule(?!FilesRoot\))') {
            $relativeProject = Get-RelativePath -BasePath $repositoryRoot -TargetPath $projectFile.FullName
            $errors.Add("$relativeProject references another reusable module through '$include'.")
        }
    }
}

$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Recurse -Filter '*.cs' -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
foreach ($sourceFile in $sourceFiles) {
    $source = Get-Content -LiteralPath $sourceFile.FullName -Raw
    $relativePath = Get-RelativePath -BasePath $repositoryRoot -TargetPath $sourceFile.FullName
    if ($source -match 'Gma\.Modules\.(?!Files(?:\.|;))') {
        $errors.Add("$relativePath names another reusable module implementation or contract.")
    }

    if ($source -match '(?:BunkFy|StayQuest)\.') {
        $errors.Add("$relativePath contains product-specific source.")
    }
}

if ($errors.Count -gt 0) {
    throw "Files boundary checks failed:`n - $($errors -join "`n - ")"
}

Write-Host 'Files boundary checks passed.'
