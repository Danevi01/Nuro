# Legt Verknuepfungen "Nuro" auf dem Desktop und im Startmenue an.
$dir = $PSScriptRoot
$ws  = New-Object -ComObject WScript.Shell
$ziele = @([Environment]::GetFolderPath('Desktop'), (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'))
foreach ($z in $ziele) {
    $s = $ws.CreateShortcut((Join-Path $z 'Nuro.lnk'))
    $s.TargetPath       = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $s.Arguments        = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$dir\Nuro.ps1`""
    $s.WorkingDirectory = $dir
    $s.IconLocation     = Join-Path $dir 'app\icon.ico'
    $s.WindowStyle      = 7
    $s.Description      = 'Nuro mit Ollama'
    $s.Save()
}
Write-Host 'Verknuepfungen "Nuro" auf dem Desktop und im Startmenue angelegt.'
