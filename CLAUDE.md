# Adisyon: Claude için notlar

- Geliştirici başlangıç seviyesinde, Türkçe konuşuyor. Açıklamalar Türkçe ve öğretici olmalı. Kod yorumları Türkçe, kod tanımlayıcıları (sınıf, metot, değişken) İngilizce.
- **Kurulum modeli: önce yerel, bulut sonra.** Restorandaki bir Windows bilgisayar sunucu olur (API + PostgreSQL, Windows servisi); diğer ekranlar yerel ağdan bağlanır. İnternet olmadan çalışmalı. QR menü ve uzaktan rapor ileride bulut modülü olarak gelecek. Geliştirici kurulumu restoranlara kendisi yapacak.
- **Giriş: e-posta yok, yalnızca 4 haneli PIN.** Cihaz bir kez şubenin eşleştirme koduyla eşleştirilir (`/api/auth/pair`, `X-Device-Token`), personel o cihazda PIN'le girer (`/api/auth/pin-login`). PIN'ler `SecretHasher.HashPin` (HMAC) ile saklanır, restoran içinde tekildir; 5 yanlış denemede cihaz 1 dk kilitlenir.
- Yol haritası: Faz 0 temeller → 1 backend çekirdek → 2 sipariş akışı (SignalR, QR menü, mutfak) → 3 kasa/yönetim → 4 yazıcı ajanı (ESC/POS) → 5 AI (Claude API) + raporlar → 6 Windows paketleme + pilot → 7 bulut modülü ve satış.
- Bilinçli olarak YOK: Avalonia, Redis, MediatR, ML.NET. Tüm ekranlar tek React uygulamasında (`/m/:qrToken`, `/mutfak`, `/kasa`, `/admin`).
- Çoklu kiracı: her tabloda `TenantId` ve EF Core global query filter (yerelde tek kiracı olur ama buluta geçiş için korunuyor). Uç noktalar varsayılan olarak giriş ister; açık olanlar `[AllowAnonymous]`.
- API `http://localhost:5260`, web `http://localhost:5173`; Vite `/api` isteklerini API'ye proxy'ler.
- Doğrulama: `dotnet test` (Docker açık olmalı, Testcontainers), `npm --prefix web run build`, `npm --prefix web run lint`.
