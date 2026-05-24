using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using ArizaSikayet.Data;
using ArizaSikayet.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Localization;
using ArizaSikayet.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "DataProtectionKeys")));

// 1. Veritabanı Bağlantısı (SQLite)
var appDataDbPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "arizalar.db");
var rootDbPath = Path.Combine(builder.Environment.ContentRootPath, "arizalar.db");
var dbPath = File.Exists(appDataDbPath)
    ? appDataDbPath
    : File.Exists(rootDbPath)
        ? rootDbPath
        : appDataDbPath;

Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
var sqliteConnectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
builder.Services.AddDbContext<UygulamaDbContext>(options =>
    options.UseSqlite("Data Source=arizalar.db"));

// 2. CORS Ayarları (Flutter ve diğer istemcilerin erişebilmesi için)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// 3. Controller ve JSON Ayarları (Tek bir yerde tanımlanmalı)
builder.Services.AddDistributedMemoryCache(); // Session verilerini saklamak için geçici hafıza
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20); // Oturum süresi
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        // API yanıtlarını camelCase yapar (Örn: Baslik -> baslik)
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        // JSON çıktısını okunabilir (indent) yapar
        options.JsonSerializerOptions.WriteIndented = true;
        // Null olan alanları JSON içinde göndermez (Bant genişliği tasarrufu)
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings")); // Bunu ekledik
builder.Services.AddScoped<EmailService>();

builder.Services.AddSingleton<ApiTokenService>();

var turkceKultur = new CultureInfo("tr-TR");
CultureInfo.DefaultThreadCurrentCulture = turkceKultur;
CultureInfo.DefaultThreadCurrentUICulture = turkceKultur;

// 4. Kimlik Doğrulama (Cookie Authentication)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
        ApiTokenAuthenticationDefaults.AuthenticationScheme,
        options => { })
    .AddCookie(options =>
    {
        options.LoginPath = "/Vatandas/Login";
        options.AccessDeniedPath = "/Admin/AccessDenied";
        options.Cookie.Name = "ArizaSikayet.Auth";
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                var path = context.Request.Path;
                if (path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                if (path.StartsWithSegments("/Admin") || path.StartsWithSegments("/Ariza/AdminPanel"))
                {
                    context.Response.Redirect("/Admin/Login");
                }
                else if (path.StartsWithSegments("/Personel"))
                {
                    context.Response.Redirect("/Personel/Login");
                }
                else
                {
                    context.Response.Redirect("/Vatandas/Login");
                }

                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                if (context.HttpContext.User.IsInRole("Admin"))
                {
                    context.Response.Redirect("/Ariza/AdminPanel");
                }
                else if (context.HttpContext.User.IsInRole("Personel"))
                {
                    context.Response.Redirect("/Personel/Panel");
                }
                else
                {
                    context.Response.Redirect("/Vatandas/Panel");
                }

                return Task.CompletedTask;
            }
        };
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UygulamaDbContext>();
    var degisiklikVar = false;

    foreach (var ariza in db.Arizalar)
    {
        var yeniKategori = ArizaKategorileri.Normalize(ariza.Kategori);
        if (!string.Equals(ariza.Kategori, yeniKategori, StringComparison.Ordinal))
        {
            ariza.Kategori = yeniKategori;
            degisiklikVar = true;
        }
    }

    foreach (var personel in db.Personeller)
    {
        var yeniKategori = ArizaKategorileri.Normalize(personel.GorevKategori);
        if (!string.Equals(personel.GorevKategori, yeniKategori, StringComparison.Ordinal))
        {
            personel.GorevKategori = yeniKategori;
            degisiklikVar = true;
        }
    }

    foreach (var istek in db.PersonelKayitIstekleri)
    {
        var yeniKategori = ArizaKategorileri.Normalize(istek.GorevKategori);
        if (!string.Equals(istek.GorevKategori, yeniKategori, StringComparison.Ordinal))
        {
            istek.GorevKategori = yeniKategori;
            degisiklikVar = true;
        }
    }

    if (degisiklikVar)
    {
        db.SaveChanges();
    }
}

// --- HTTP Request Pipeline (Middleware Sıralaması Önemlidir) ---

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(turkceKultur),
    SupportedCultures = new[] { turkceKultur },
    SupportedUICultures = new[] { turkceKultur }
});

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.ContentLanguage = "tr-TR";

        if (context.Response.ContentType != null &&
            context.Response.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) &&
            !context.Response.ContentType.Contains("charset", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = $"{context.Response.ContentType}; charset=utf-8";
        }

        return Task.CompletedTask;
    });

    await next();
});

// 5. Statik Dosyalar (Resimlerin /uploads klasöründen okunabilmesi için ŞART)
app.UseStaticFiles(); 

app.UseRouting();

// 6. CORS (Routing'den sonra, Auth'dan önce gelmeli)
app.UseCors("AllowAll");

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// 7. API Controller'larını Haritala ([ApiController] özniteliği olan sınıflar için)
app.MapControllers();

// 8. MVC Rotalarını Haritala
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// .NET 8/9 için gelişmiş statik varlık haritalama (Opsiyonel)
if (app.Environment.IsDevelopment())
{
    app.MapStaticAssets();
}

app.Run();
