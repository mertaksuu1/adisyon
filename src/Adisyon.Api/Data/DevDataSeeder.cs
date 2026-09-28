using System.Security.Cryptography;
using Adisyon.Api.Auth;
using Adisyon.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Data;

/// <summary>
/// Geliştirme ortamında boş veritabanına örnek bir restoran yükler: şube, masalar, menü ve personel.
/// Yalnızca Development ortamında çalışır; zaten yüklenmiş parçaları atlar.
/// </summary>
public static class DevDataSeeder
{
    /// <summary>Merkez Şube'nin eşleştirme kodu. SADECE geliştirme içindir; gerçek kurulumda rastgele üretilir.</summary>
    public const string DemoPairingCode = "DEMO-DEMO-DEMO";

    /// <summary>
    /// Demo personel ve PIN'leri. SADECE geliştirme içindir; üretimde bu yükleyici çalışmaz.
    /// </summary>
    public static readonly (string Name, UserRole Role, string Pin)[] DemoStaff =
    [
        ("Ayşe Sahip", UserRole.Owner, "1111"),
        ("Mehmet Yönetici", UserRole.Manager, "2222"),
        ("Ali Garson", UserRole.Waiter, "3333"),
        ("Fatma Mutfak", UserRole.Kitchen, "4444"),
        ("Can Kasa", UserRole.Cashier, "5555"),
    ];

    public static async Task SeedAsync(AdisyonDbContext db, SecretHasher secretHasher)
    {
        await SeedRestaurantAsync(db);
        await SeedStaffAsync(db, secretHasher);
    }

    private static async Task SeedStaffAsync(AdisyonDbContext db, SecretHasher secretHasher)
    {
        // Kiracı filtresi yükleyicide aktif kiracı olmadığı için her şeyi gizler; burada bilerek kapatıyoruz.
        if (await db.Users.IgnoreQueryFilters().AnyAsync())
        {
            return;
        }

        var tenant = await db.Tenants.SingleAsync(t => t.Slug == "demo");
        var branch = await db.Branches.IgnoreQueryFilters().FirstAsync(b => b.TenantId == tenant.Id);
        branch.PairingCodeHash = SecretHasher.HashPairingCode(DemoPairingCode);

        foreach (var (name, role, pin) in DemoStaff)
        {
            db.Users.Add(new User
            {
                TenantId = tenant.Id,
                DisplayName = name,
                PinHash = secretHasher.HashPin(tenant.Id, pin),
                Role = role,
                // Sahip tüm şubelere erişir; diğerleri Merkez Şube'de çalışır.
                BranchId = role == UserRole.Owner ? null : branch.Id,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedRestaurantAsync(AdisyonDbContext db)
    {
        if (await db.Tenants.AnyAsync())
        {
            return;
        }

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Demo Restoran", Slug = "demo", CreatedAt = DateTimeOffset.UtcNow };
        db.Tenants.Add(tenant);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Merkez Şube", Address = "Kadıköy, İstanbul" };
        db.Branches.Add(branch);

        for (var i = 1; i <= 8; i++)
        {
            db.Tables.Add(new DiningTable
            {
                TenantId = tenant.Id,
                BranchId = branch.Id,
                Name = $"Masa {i}",
                QrToken = NewQrToken(),
                SortOrder = i,
            });
        }

        var menu = new (string Category, (string Name, decimal Price, string? Description)[] Products)[]
        {
            ("Çorbalar", [("Mercimek Çorbası", 90m, "Limon ve kıtır ekmekle"), ("Ezogelin Çorbası", 90m, null)]),
            ("Ana Yemekler", [("Adana Kebap", 380m, "Közlenmiş biber ve domatesle"), ("Tavuk Şiş", 290m, null), ("Karışık Izgara", 520m, null)]),
            ("Tatlılar", [("Künefe", 180m, "Antep fıstıklı"), ("Sütlaç", 120m, null)]),
            ("İçecekler", [("Ayran", 40m, null), ("Çay", 25m, null), ("Kola", 60m, "330 ml")]),
        };

        var categoryOrder = 0;
        foreach (var (categoryName, products) in menu)
        {
            var category = new Category { TenantId = tenant.Id, Name = categoryName, SortOrder = ++categoryOrder };
            var productOrder = 0;
            foreach (var (name, price, description) in products)
            {
                category.Products.Add(new Product
                {
                    TenantId = tenant.Id,
                    Name = name,
                    Price = price,
                    Description = description,
                    SortOrder = ++productOrder,
                });
            }
            db.Categories.Add(category);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Kriptografik olarak rastgele, URL'de güvenle kullanılabilen 16 karakterlik anahtar.</summary>
    public static string NewQrToken() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
}
