using Adisyon.Api.Data;
using Adisyon.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<TenantContext>();

// Bağlantı dizesi appsettings.Development.json içinde (testlerde test veritabanınınki kullanılır).
builder.Services.AddDbContext<AdisyonDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Adisyon")
        ?? throw new InvalidOperationException("ConnectionStrings:Adisyon ayarı eksik.");
    options.UseNpgsql(connectionString)
        // C#'taki "OrderItem" veritabanında "order_items" olur; PostgreSQL geleneği budur.
        .UseSnakeCaseNamingConvention();
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // API dokümantasyonu: http://localhost:5260/scalar
    app.MapScalarApiReference();

    // Geliştirmede uygulama açılırken bekleyen migration'ları uygula ve demo veriyi yükle.
    // Üretimde bunu otomatik yapmayacağız; migration'lar kontrollü şekilde çalıştırılacak.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AdisyonDbContext>();
    await db.Database.MigrateAsync();
    await DevDataSeeder.SeedAsync(db);
}

// HTTPS yönlendirmesi yok: üretimde TLS'i önündeki Caddy sunucusu yapacak.

app.UseAuthorization();

app.MapControllers();

app.Run();
