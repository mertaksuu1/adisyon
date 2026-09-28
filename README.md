# Adisyon

Yapay zeka destekli, QR menülü restoran adisyon sistemi (SaaS).

## Klasör yapısı

```
src/Adisyon.Api/          ASP.NET Core (.NET 10) Web API
tests/Adisyon.Api.Tests/  API testleri (xUnit)
web/                      React + Vite + TypeScript + Tailwind arayüzü
docker-compose.yml        Geliştirme servisleri (PostgreSQL)
```

## Gereksinimler

- .NET 10 SDK
- Node.js 20.19+ (npm ile)
- Docker Desktop

## Çalıştırma

Üç ayrı terminalde:

```bash
# 1) Veritabanı (Docker Desktop açık olmalı)
docker compose up -d

# 2) API → http://localhost:5260  (dokümantasyon: http://localhost:5260/scalar)
dotnet run --project src/Adisyon.Api

# 3) Web arayüzü → http://localhost:5173
cd web
npm install   # yalnızca ilk seferde
npm run dev
```

http://localhost:5173 adresindeki sayfada Web, API ve Veritabanı satırlarının üçü de "Çalışıyor" göstermeli.

## Testler

```bash
dotnet test
cd web && npm run build && npm run lint
```
