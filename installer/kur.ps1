# Adisyon kurulum / güncelleme betiği. Kur.cmd tarafından yönetici olarak çalıştırılır.
#   - Programı C:\Program Files\Adisyon klasörüne kopyalar (güncellemede eskisinin üzerine yazar)
#   - "Adisyon" Windows servisini kaydeder: bilgisayar açılınca kendiliğinden başlar, çökerse yeniden başlar
#   - Güvenlik duvarında 5000 portunu açar (restorandaki tabletler bağlanabilsin)
#   - Masaüstüne "Adisyon" kısayolu koyar (Edge uygulama modu: adres çubuğu olmadan açılır)
# Veriler C:\ProgramData\Adisyon klasöründedir; kurulum ve güncelleme onlara DOKUNMAZ.

$ErrorActionPreference = 'Stop'
$ServiceName = 'Adisyon'
$InstallDir  = Join-Path $env:ProgramFiles 'Adisyon'
$Port        = 5000
$Exe         = Join-Path $InstallDir 'Adisyon.Api.exe'

function Step($text) { Write-Host "`n>> $text" -ForegroundColor Cyan }

Step 'Program durduruluyor (güncelleme ise)...'
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') {
    Stop-Service -Name $ServiceName -Force
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

Step "Dosyalar kopyalanıyor: $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'program\*') -Destination $InstallDir -Recurse -Force

if (-not $service) {
    Step 'Windows servisi kaydediliyor...'
    New-Service -Name $ServiceName -BinaryPathName "`"$Exe`"" -DisplayName 'Adisyon' `
        -Description 'Adisyon restoran sistemi' -StartupType Automatic | Out-Null
    # Program beklenmedik şekilde kapanırsa 1 dakika sonra yeniden başlat.
    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
}

Step "Güvenlik duvarında $Port portu açılıyor..."
if (-not (Get-NetFirewallRule -DisplayName 'Adisyon' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'Adisyon' -Direction Inbound -Protocol TCP -LocalPort $Port `
        -Action Allow -Profile Private,Domain | Out-Null
}

Step 'Program başlatılıyor...'
Start-Service -Name $ServiceName
$ok = $false
for ($i = 0; $i -lt 60; $i++) {
    try {
        $health = Invoke-RestMethod "http://localhost:$Port/api/health" -TimeoutSec 2
        if ($health.api -eq 'ok') { $ok = $true; break }
    } catch { }
    Start-Sleep -Seconds 1
}
if (-not $ok) {
    Write-Host "`nProgram başlatılamadı. Olay Görüntüleyicisi > Windows Günlükleri > Uygulama bölümünde 'Adisyon' kayıtlarına bakın." -ForegroundColor Red
    exit 1
}

Step 'Masaüstü kısayolu oluşturuluyor...'
$desktop = [Environment]::GetFolderPath('CommonDesktopDirectory')
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
          "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($edge) {
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $desktop 'Adisyon.lnk'))
    $shortcut.TargetPath = $edge
    $shortcut.Arguments  = "--app=http://localhost:$Port"
    $shortcut.Save()
} else {
    Set-Content -Path (Join-Path $desktop 'Adisyon.url') -Value "[InternetShortcut]`r`nURL=http://localhost:$Port" -Encoding ASCII
}

Write-Host "`nKurulum tamamlandı." -ForegroundColor Green
Write-Host "Bu bilgisayarda: masaüstündeki 'Adisyon' kısayolu."
$addresses = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
    ForEach-Object { "http://$($_.IPAddress):$Port" }
if ($addresses) {
    Write-Host 'Restorandaki tablet ve diğer bilgisayarlar şu adresi açsın:'
    $addresses | ForEach-Object { Write-Host "   $_" -ForegroundColor Yellow }
}
$public = Get-NetConnectionProfile -ErrorAction SilentlyContinue | Where-Object { $_.NetworkCategory -eq 'Public' }
if ($public) {
    Write-Host "`nUYARI: Bu bilgisayarın ağı 'Ortak (Public)' olarak ayarlı; tabletler bağlanamayabilir." -ForegroundColor Yellow
    Write-Host "Ayarlar > Ağ ve İnternet > Wi-Fi/Ethernet > ağ profili: 'Özel (Private)' yapın."
}

if ($edge) { Start-Process $edge "--app=http://localhost:$Port" } else { Start-Process "http://localhost:$Port" }
