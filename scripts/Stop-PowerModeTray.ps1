$ErrorActionPreference = 'Continue'
$pidPaths = @(
  (Join-Path $env:LOCALAPPDATA 'MicaLovesKPOP\PowerMode\power-mode-tray-winui.pid'),
  (Join-Path $env:LOCALAPPDATA 'MicaLovesKPOP\PowerMode\power-mode-tray-native.pid'),
  (Join-Path $env:ProgramData 'MicaLovesKPOP\PowerModeTray\power-mode-tray-winui.pid'),
  (Join-Path $env:ProgramData 'MicaLovesKPOP\PowerModeTray\power-mode-tray-native.pid'),
  (Join-Path $env:ProgramData 'MicaLovesKPOP\PowerModeTray\power-mode-tray.pid')
)
foreach ($pidPath in $pidPaths) {
  if (Test-Path -LiteralPath $pidPath) {
    $pidText = (Get-Content -LiteralPath $pidPath -Raw -ErrorAction SilentlyContinue).Trim()
    if ($pidText -match '^\d+$') {
      Get-Process -Id ([int]$pidText) -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $pidPath -Force -ErrorAction SilentlyContinue
  }
}
Get-Process PowerModeTray -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "name = 'powershell.exe' OR name = 'pwsh.exe'" -ErrorAction SilentlyContinue |
  Where-Object { $_.CommandLine -like '*PowerModeTray.ps1*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Write-Host 'Power Mode tray stopped.'
