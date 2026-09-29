# Adisyon kaldırma betiği. Kaldir.cmd tarafından yönetici olarak çalıştırılır.
# Programı, servisi, güvenlik duvarı kuralını ve kısayolu kaldırır.
# VERİLER SİLİNMEZ: C:\ProgramData\Adisyon (veritabanı, gizli anahtarlar, yedekler) yerinde kalır.

$ErrorActionPreference = 'Continue'
$ServiceName = 'Adisyon'

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
}
Remove-NetFirewallRule -DisplayName 'Adisyon' -ErrorAction SilentlyContinue
$desktop = [Environment]::GetFolderPath('CommonDesktopDirectory')
Remove-Item (Join-Path $desktop 'Adisyon.lnk'), (Join-Path $desktop 'Adisyon.url') -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Remove-Item (Join-Path $env:ProgramFiles 'Adisyon') -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`nAdisyon kaldırıldı." -ForegroundColor Green
Write-Host "Veriler duruyor: $env:ProgramData\Adisyon  (tamamen silmek isterseniz bu klasörü elle silin)"
