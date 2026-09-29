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

`dist/Adisyon-win-x64.zip` oluşur. Kasa bilgisayarında ZIP'i açıp **Kur.cmd**'ye çift tıklayın (yönetici izni ister):
program `C:\Program Files\Adisyon`'a kurulur, "Adisyon" Windows servisi olarak bilgisayar açılınca kendiliğinden başlar,
güvenlik duvarında 5000 portu açılır ve masaüstüne "Adisyon" kısayolu konur. Güncelleme için yeni paketteki Kur.cmd tekrar çalıştırılır.
Ayrıntılar paketteki BENIOKU.txt dosyasında.

- Veriler `C:\ProgramData\Adisyon`: `adisyon.db` (SQLite), `secrets.json` (**kaybolursa PIN'ler çalışmaz**), `yedekler/` (her gün otomatik, son 30).
- İlk açılışta tarayıcıda ilk kurulum sihirbazı açılır (restoran, şube, işletme sahibi ve PIN).

## Yapay zeka özellikleri (isteğe bağlı)

Gün sonu raporu yorumu ve "Rapora sor" sayfası Claude API kullanır; internet ve bir API anahtarı gerekir.
Anahtar yoksa bu özellikler "ayarlanmamış" der, programın geri kalanı etkilenmez.

- Geliştirmede: `ANTHROPIC_API_KEY` ortam değişkeni.
- Kurulumda: `C:\ProgramData\Adisyon\appsettings.Local.json` dosyası, ardından Adisyon servisini yeniden başlatın:

```json
{ "Anthropic": { "ApiKey": "sk-ant-..." } }
```
