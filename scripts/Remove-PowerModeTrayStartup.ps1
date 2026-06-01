$ErrorActionPreference = 'SilentlyContinue'

$startupFolder = [Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startupFolder 'Power Mode.lnk'
Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction SilentlyContinue

schtasks.exe /Delete /TN 'Power Mode' /F | Out-Null

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $runKey -Name 'Power Mode' -Force -ErrorAction SilentlyContinue

$approvedKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
Remove-ItemProperty -Path $approvedKey -Name 'Power Mode' -Force -ErrorAction SilentlyContinue

Write-Host "Power Mode startup entries removed."
