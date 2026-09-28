# Adisyon: Claude için notlar

- Geliştirici başlangıç seviyesinde, Türkçe konuşuyor. Açıklamalar Türkçe ve öğretici olmalı. Kod yorumları Türkçe, kod tanımlayıcıları (sınıf, metot, değişken) İngilizce.
- Yol haritası: Faz 0 temeller → 1 backend çekirdek → 2 sipariş akışı (SignalR, QR menü, mutfak) → 3 kasa/yönetim → 4 yazıcı ajanı (ESC/POS) → 5 AI (Claude API) + raporlar → 6 pilot → 7 SaaS (abonelik, iyzico, ÖKC/e-Adisyon).
- Bilinçli olarak YOK: Avalonia, Redis, MediatR, ML.NET. Tüm ekranlar tek React uygulamasında (`/m/:qrToken`, `/mutfak`, `/kasa`, `/admin`).
- Çoklu kiracı: Faz 1'den itibaren her tabloda `TenantId` ve EF Core global query filter.
- API `http://localhost:5260`, web `http://localhost:5173`; Vite `/api` isteklerini API'ye proxy'ler.
- Doğrulama: `dotnet test`, `npm --prefix web run build`, `npm --prefix web run lint`.
