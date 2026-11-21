# Legt Verknuepfungen "Lokal-Chat" auf dem Desktop und im Startmenue an.
$dir = $PSScriptRoot
$ws  = New-Object -ComObject WScript.Shell
$ziele = @([Environment]::GetFolderPath('Desktop'), (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'))
foreach ($z in $ziele) {
    $s = $ws.CreateShortcut((Join-Path $z 'Lokal-Chat.lnk'))
    $s.TargetPath       = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $s.Arguments        = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$dir\LokalChat.ps1`""
    $s.WorkingDirectory = $dir
    $s.IconLocation     = Join-Path $dir 'app\icon.ico'
    $s.WindowStyle      = 7
    $s.Description      = 'Lokal-Chat mit Ollama'
    $s.Save()
}
Write-Host 'Verknuepfungen "Lokal-Chat" auf dem Desktop und im Startmenue angelegt.'
