# Adisyon

Yapay zeka destekli, QR menülü restoran adisyon sistemi (SaaS).

## Klasör yapısı

```
src/Adisyon.Api/          ASP.NET Core (.NET 10) Web API
tests/Adisyon.Api.Tests/  API testleri (xUnit)
web/                      React + Vite + TypeScript + Tailwind arayüzü
```

## Gereksinimler

- .NET 10 SDK
- Node.js 20.19+ (npm ile)

## Çalıştırma

İki ayrı terminalde:

```bash
# 1) API (veritabanı src/Adisyon.Api/adisyon-dev.db dosyasıdır; ilk açılışta oluşur) → http://localhost:5260  (dokümantasyon: http://localhost:5260/scalar)
dotnet run --project src/Adisyon.Api

# 2) Web arayüzü → http://localhost:5173
cd web
npm install   # yalnızca ilk seferde
npm run dev
```

http://localhost:5173 adresindeki sayfada Web, API ve Veritabanı satırlarının üçü de "Çalışıyor" göstermeli.

İlk açılışta API veritabanı tablolarını kendisi oluşturur ve "Demo Restoran" örnek verisini (8 masa, 10 ürünlük menü, her rolden bir personel) yükler.

## Giriş nasıl çalışır?

E-posta yok; personel 4 haneli PIN ile girer.

1. **Cihaz eşleştirme (bir kez):** Bilgisayar, şubenin eşleştirme koduyla `POST /api/auth/pair` üzerinden restorana bağlanır ve bir cihaz anahtarı alır.
2. **PIN girişi:** Eşleştirilmiş cihaz, anahtarını `X-Device-Token` başlığında göndererek `POST /api/auth/pin-login` ile PIN'i doğrular. PIN'e göre kişinin rolü (sahip, yönetici, garson, mutfak, kasa) belirlenir.

Demo eşleştirme kodu ve personel PIN'leri: [DevDataSeeder.cs](src/Adisyon.Api/Data/DevDataSeeder.cs) (yalnızca geliştirme ortamı).

## Veritabanı değişiklikleri (migration)

`src/Adisyon.Api/Domain` altındaki bir sınıfı değiştirdikten sonra:

```bash
dotnet tool restore   # yalnızca ilk seferde
dotnet ef migrations add DegisikliginAdi --project src/Adisyon.Api --output-dir Data/Migrations
```

API bir sonraki açılışta migration'ı uygular. Demo veriyi sıfırlamak için API'yi kapatıp `src/Adisyon.Api/adisyon-dev.db*` dosyalarını silin.

## Testler

Testler her çalıştırmada geçici bir SQLite veritabanı dosyası kullanır.

```bash
dotnet test
cd web && npm run build && npm run lint
```

## Restoran için kurulum paketi

```bash
scripts/build-package.sh win-x64
```

`dist/adisyon-win-x64/` klasörü oluşur: web ekranları dahil tek program, .NET kurulumu gerektirmez. Program ilk açıldığında:

- `data/secrets.json` içinde bu kuruluma özel gizli anahtarlar üretilir. **Bu dosya veritabanıyla birlikte yedeklenmeli**; kaybolursa PIN'ler çalışmaz.
- Veritabanı boşsa tarayıcıda ilk kurulum sihirbazı açılır (restoran, şube, işletme sahibi ve PIN).

Veritabanı `data/adisyon.db` dosyasıdır (SQLite; ayrı veritabanı kurulumu gerekmez). **Yedek almak için `data` klasörünün tamamını kopyalayın**; gizli anahtarlar da oradadır.
