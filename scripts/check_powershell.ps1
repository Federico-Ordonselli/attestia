# Analisi statica degli script PowerShell (eseguibile su Linux con l'immagine Docker ufficiale):
#   docker run --rm -v "$PWD:/w:ro" -w /w mcr.microsoft.com/dotnet/sdk:10.0 pwsh -NoProfile -File scripts/check_powershell.ps1
# 1) parser AST: nessun errore di sintassi; 2) PSScriptAnalyzer: nessun errore o avviso.
$ErrorActionPreference = 'Stop'
$failed = $false
$scripts = Get-ChildItem -Path deploy -Recurse -Filter *.ps1
foreach ($script in $scripts) {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors)
    foreach ($e in $errors) { Write-Host "ERRORE DI SINTASSI $($script.Name):$($e.Extent.StartLineNumber) $($e.Message)"; $failed = $true }
}
if (-not (Get-Module -ListAvailable PSScriptAnalyzer)) {
    Install-Module PSScriptAnalyzer -Scope CurrentUser -Force | Out-Null
}
# PSAvoidUsingWriteHost: lo script di installazione scrive messaggi per l'operatore, non dati di pipeline.
$findings = Invoke-ScriptAnalyzer -Path deploy -Recurse -Severity Error, Warning -ExcludeRule PSAvoidUsingWriteHost
foreach ($f in $findings) { Write-Host "$($f.Severity) $($f.ScriptName):$($f.Line) [$($f.RuleName)] $($f.Message)"; $failed = $true }
if ($failed) { exit 1 }
Write-Host "PowerShell: $($scripts.Count) script analizzati, nessun problema."
