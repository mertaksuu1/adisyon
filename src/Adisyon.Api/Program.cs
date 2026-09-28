using Npgsql;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// PostgreSQL bağlantısı. Bağlantı dizesi appsettings.Development.json içinde.
// Faz 1'de bunun yerine EF Core DbContext gelecek.
var connectionString = builder.Configuration.GetConnectionString("Adisyon")
    ?? throw new InvalidOperationException("ConnectionStrings:Adisyon ayarı eksik.");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // API dokümantasyonu: http://localhost:5260/scalar
    app.MapScalarApiReference();
}

// HTTPS yönlendirmesi yok: üretimde TLS'i önündeki Caddy sunucusu yapacak.

app.UseAuthorization();

app.MapControllers();

app.Run();
