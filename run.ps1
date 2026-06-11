$ErrorActionPreference = "Stop"
$projectFiles = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter "*.csproj" -File)
if ($projectFiles.Count -ne 1) {
    throw "Expected exactly one .csproj file in $PSScriptRoot, found $($projectFiles.Count)."
}

dotnet run --project $projectFiles[0].FullName
