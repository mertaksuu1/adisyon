using System.Reflection;
using Adisyon.Api.Domain;
using Adisyon.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Adisyon.Api.Data;

/// <summary>
/// Veritabanına açılan kapı. Her DbSet bir tabloyu temsil eder.
/// </summary>
public class AdisyonDbContext(DbContextOptions<AdisyonDbContext> options, TenantContext tenantContext)
    : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<DiningTable> Tables => Set<DiningTable>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<TableSession> TableSessions => Set<TableSession>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>
    /// Sorgu filtresinde kullanılır. Kiracı yoksa Guid.Empty döner; hiçbir kayıt Guid.Empty'ye ait
    /// olmadığı için sonuç boş gelir. Yani bir şey unutulursa veri sızmaz, sadece görünmez.
    /// </summary>
    private Guid CurrentTenantId => tenantContext.TenantId ?? Guid.Empty;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite'ın tarih-saat türü yok; DateTimeOffset'i sayı olarak saklıyoruz. Böylece "05:00'ten sonra"
        // gibi karşılaştırmalar ve sıralamalar veritabanında yapılabiliyor. (Değerler hep UTC kaydedilir.)
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(100);
            e.HasIndex(x => x.Slug).IsUnique();
        });

        modelBuilder.Entity<Branch>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.PairingCodeHash).HasMaxLength(64);
            e.Property(x => x.KitchenPrinterAddress).HasMaxLength(100);
            e.Property(x => x.ReceiptPrinterAddress).HasMaxLength(100);
            e.Property(x => x.PrinterCodePage).HasDefaultValue(13);
            e.HasIndex(x => x.PairingCodeHash).IsUnique();
        });

        modelBuilder.Entity<DiningTable>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.QrToken).HasMaxLength(64);
            // QR anahtarı tüm sistemde tekil: müşteri geldiğinde henüz kiracı bilinmiyor,
            // masayı (ve dolayısıyla kiracıyı) bu anahtardan buluyoruz.
            e.HasIndex(x => x.QrToken).IsUnique();
            e.HasOne(x => x.Branch).WithMany(b => b.Tables).HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Price).HasPrecision(10, 2);
            e.HasOne(x => x.Category).WithMany(c => c.Products).HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TableSession>(e =>
        {
            // Enum'ları sayı yerine metin olarak saklıyoruz; veritabanına bakınca "Open" okumak "0"dan kolay.
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            // Sürüm sayacı: UPDATE ... WHERE version = <istemcinin gördüğü> şeklinde kontrol edilir.
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<TableSession>().WithMany().HasForeignKey(x => x.MergedIntoSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Table).WithMany().HasForeignKey(x => x.TableId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
            // Bir masada aynı anda yalnızca bir açık adisyon olabilir. Bunu veritabanı garanti ediyor.
            e.HasIndex(x => x.TableId).IsUnique().HasFilter("status = 'Open'");
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.TableSession).WithMany(s => s.Orders).HasForeignKey(x => x.TableSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.Property(x => x.ProductName).HasMaxLength(200);
            e.Property(x => x.UnitPrice).HasPrecision(10, 2);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne<Order>().WithMany(o => o.Items).HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.VoidedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CompedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.IsCharged); // hesaplanan özellik, veritabanında kolonu yok
        });

        modelBuilder.Entity<User>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.PinHash).HasMaxLength(64);
            // Aynı restoranda iki kişi aynı PIN'i kullanamaz. Veritabanı da bunu garanti ediyor.
            e.HasIndex(x => new { x.TenantId, x.PinHash }).IsUnique();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(e =>
        {
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Amount).HasPrecision(10, 2);
            e.HasOne<TableSession>().WithMany(s => s.Payments).HasForeignKey(x => x.TableSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Device>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ITenantOwned uygulayan her varlığa kiracı filtresini ve tenant_id indeksini ekle.
        // Döngüyle yapıyoruz ki yeni bir varlık eklendiğinde filtre unutulmasın.
        var applyFilter = typeof(AdisyonDbContext)
            .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                applyFilter.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder) where T : class, ITenantOwned
    {
        modelBuilder.Entity<T>().HasQueryFilter(x => x.TenantId == CurrentTenantId);
        modelBuilder.Entity<T>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<T>().HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTenantRules();
        BumpSessionVersions();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyTenantRules();
        BumpSessionVersions();
        return base.SaveChanges();
    }

    /// <summary>
    /// Değişen her adisyonun sürümünü bir artırır. EF, UPDATE'e "WHERE version = eski sürüm" ekler;
    /// o arada başka biri kaydettiyse hiçbir satır güncellenmez ve DbUpdateConcurrencyException oluşur.
    /// </summary>
    private void BumpSessionVersions()
    {
        foreach (var entry in ChangeTracker.Entries<TableSession>().Where(e => e.State == EntityState.Modified))
        {
            var version = entry.Property(x => x.Version);
            version.CurrentValue = version.OriginalValue + 1;
        }
    }

    /// <summary>
    /// Kaydetmeden hemen önce çalışır:
    /// - Yeni kayıtlarda TenantId boşsa aktif kiracıyla doldurur.
    /// - Aktif kiracıdan farklı bir kiracıya yazmayı ve TenantId değiştirmeyi engeller.
    /// Kiracı yoksa (ör. demo veri yükleyici) TenantId'nin açıkça verilmiş olması gerekir.
    /// </summary>
    private void ApplyTenantRules()
    {
        var current = tenantContext.TenantId;

        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.TenantId == Guid.Empty)
                {
                    entry.Entity.TenantId = current
                        ?? throw new InvalidOperationException(
                            $"{entry.Entity.GetType().Name} kaydedilemedi: kiracı belirlenmemiş.");
                }
                else if (current is not null && entry.Entity.TenantId != current)
                {
                    throw new InvalidOperationException("Başka bir kiracıya ait kayıt oluşturulamaz.");
                }
            }
            else if (entry.State == EntityState.Modified && entry.Property(x => x.TenantId).IsModified)
            {
                throw new InvalidOperationException("Bir kaydın kiracısı değiştirilemez.");
            }
        }
    }
}
