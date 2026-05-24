using System.Security.Claims;
using System.Security.Cryptography;
using ArizaSikayet.Data;
using ArizaSikayet.Models;
using ArizaSikayet.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArizaSikayet.Controllers.Api;

[ApiController]
[Route("api/mobile")]
public class MobileApiController : ControllerBase
{
    private const long MaksimumFotografBoyutu = 10 * 1024 * 1024;
    private static readonly string[] IzinliFotografUzantilari = { ".jpg", ".jpeg", ".png" };
    private static readonly string[] IzinliFotografIcerikTipleri = { "image/jpeg", "image/png", "image/jpg" };

    private readonly UygulamaDbContext _db;
    private readonly EmailService _emailService;
    private readonly IWebHostEnvironment _env;
    private readonly ApiTokenService _apiTokenService;

    public MobileApiController(
        UygulamaDbContext db,
        EmailService emailService,
        IWebHostEnvironment env,
        ApiTokenService apiTokenService)
    {
        _db = db;
        _emailService = emailService;
        _env = env;
        _apiTokenService = apiTokenService;
    }

    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        var veritabaniHazir = await _db.Database.CanConnectAsync();

        return Ok(new
        {
            success = true,
            status = "ok",
            database = veritabaniHazir,
            publicMapEndpoint = "/Ariza/GetArizalar",
            authenticatedApiBase = "/api/mobile",
            serverTime = DateTimeOffset.UtcNow
        });
    }

    [HttpGet("kategoriler")]
    public IActionResult Kategoriler()
    {
        var kategoriler = ArizaKategorileri.TumKategoriler.Select(kategori => new
        {
            value = kategori,
            label = ArizaKategorileri.Etiketler[kategori],
            iconLabel = ArizaKategorileri.IconluEtiketler[kategori]
        });

        return Ok(new { success = true, data = kategoriler });
    }

    [HttpPost("login")]
    [HttpPost("auth/login")]
    public Task<IActionResult> Login([FromBody] MobileLoginRequest request)
    {
        return LoginAsync(request, KullaniciRolunuOku(request));
    }

    [HttpPost("vatandas/login")]
    public Task<IActionResult> VatandasLogin([FromBody] MobileLoginRequest request)
    {
        return LoginAsync(request, "Vatandas");
    }

    [HttpPost("personel/login")]
    public Task<IActionResult> PersonelLogin([FromBody] MobileLoginRequest request)
    {
        return LoginAsync(request, "Personel");
    }

    [HttpPost("admin/login")]
    public async Task<IActionResult> AdminLogin([FromBody] MobileAdminLoginRequest request)
    {
        var kullaniciAdi = (request.KullaniciAdi ?? request.Eposta ?? "").Trim();
        if (string.IsNullOrWhiteSpace(kullaniciAdi) || string.IsNullOrWhiteSpace(request.Sifre))
        {
            return BadRequest(new { success = false, message = "Kullanici adi ve sifre zorunludur." });
        }

        var admin = await _db.Adminler.FirstOrDefaultAsync(x => x.KullaniciAdi == kullaniciAdi && x.Sifre == request.Sifre);
        if (admin == null)
        {
            return Unauthorized(new { success = false, message = "Kullanici adi veya sifre hatali." });
        }

        var claims = AdminClaims(admin);
        var token = await OturumAcVeTokenOlustur(claims);

        return Ok(new
        {
            success = true,
            role = "Admin",
            message = "Giris basarili.",
            token = token.Token,
            expiresAt = token.ExpiresAt,
            data = AdminDto(admin)
        });
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Admin")]
    [HttpGet("admin/personeller")]
    public async Task<IActionResult> AdminPersoneller()
    {
        var personeller = await _db.Personeller
            .AsNoTracking()
            .OrderByDescending(x => x.OlusturmaTarihi)
            .Select(x => PersonelDto(x))
            .ToListAsync();

        return Ok(new { success = true, data = personeller });
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Admin")]
    [HttpPost("admin/personel/olustur")]
    public async Task<IActionResult> AdminPersonelOlustur([FromBody] MobilePersonelKayitRequest request)
    {
        var temizEposta = EpostaTemizle(request.Eposta);
        var temizKullaniciAdi = request.KullaniciAdi?.Trim();
        var gorevKategori = ArizaKategorileri.Normalize(request.GorevKategori);

        if (string.IsNullOrWhiteSpace(temizKullaniciAdi) ||
            string.IsNullOrWhiteSpace(temizEposta) ||
            string.IsNullOrWhiteSpace(request.Sifre))
        {
            return BadRequest(new { success = false, message = "Kullanici adi, e-posta ve sifre zorunludur." });
        }

        if (!SifrelerUyusuyor(request.Sifre, request.SifreTekrar))
        {
            return BadRequest(new { success = false, message = "Sifreler birbiriyle uyusmuyor." });
        }

        if (!ArizaKategorileri.GecerliMi(gorevKategori))
        {
            return BadRequest(new { success = false, message = "Gecerli bir gorev kategorisi secin." });
        }

        if (EpostaEngelliMi(temizEposta))
        {
            return BadRequest(new { success = false, message = "Bu e-posta adresiyle kayit yapilamaz." });
        }

        if (await _db.Personeller.AnyAsync(x => x.KullaniciAdi == temizKullaniciAdi) ||
            await _db.Personeller.AnyAsync(x => x.Eposta == temizEposta) ||
            await _db.Vatandaslar.AnyAsync(x => x.Eposta == temizEposta))
        {
            return Conflict(new { success = false, message = "Bu kullanici adi veya e-posta zaten kullaniliyor." });
        }

        var personel = new Personel
        {
            KullaniciAdi = temizKullaniciAdi,
            Eposta = temizEposta,
            Sifre = request.Sifre,
            GorevKategori = gorevKategori,
            OlusturmaTarihi = DateTime.Now
        };

        _db.Personeller.Add(personel);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "Personel olusturuldu.",
            data = PersonelDto(personel)
        });
    }

    [HttpPost("register")]
    [HttpPost("kayit")]
    [HttpPost("auth/register")]
    [HttpPost("vatandas/kayit")]
    [HttpPost("vatandas/register")]
    public async Task<IActionResult> VatandasKayit([FromBody] MobileVatandasKayitRequest request)
    {
        var temizEposta = EpostaTemizle(request.Eposta);
        var temizKullaniciAdi = request.KullaniciAdi?.Trim();

        if (string.IsNullOrWhiteSpace(temizKullaniciAdi) ||
            string.IsNullOrWhiteSpace(temizEposta) ||
            string.IsNullOrWhiteSpace(request.Sifre))
        {
            return BadRequest(new { success = false, message = "Kullanıcı adı, e-posta ve şifre zorunludur." });
        }

        if (!SifrelerUyusuyor(request.Sifre, request.SifreTekrar))
        {
            return BadRequest(new { success = false, message = "Şifreler birbiriyle uyuşmuyor." });
        }

        if (EpostaEngelliMi(temizEposta))
        {
            return BadRequest(new { success = false, message = "Bu e-posta adresiyle kayıt yapılamaz." });
        }

        if (await _db.Vatandaslar.AnyAsync(x => x.Eposta == temizEposta) ||
            await _db.Personeller.AnyAsync(x => x.Eposta == temizEposta))
        {
            return Conflict(new { success = false, message = "Bu e-posta adresiyle kayıtlı bir hesap var." });
        }

        var token = TokenUret();
        var vatandas = new Vatandas
        {
            KullaniciAdi = temizKullaniciAdi,
            Eposta = temizEposta,
            Sifre = request.Sifre,
            KayitOnayToken = token,
            EpostaOnaylandi = false,
            OlusturmaTarihi = DateTime.Now
        };

        _db.Vatandaslar.Add(vatandas);
        await _db.SaveChangesAsync();

        var url = Url.Action("KayitTamamla", "Vatandas", new { token }, Request.Scheme)!;
        await _emailService.GonderAsync(
            temizEposta,
            "Vatandaş kaydınızı tamamlayın",
            "Kaydınızı tamamlayın",
            "Arıza Şikayet hesabınızı etkinleştirmek için aşağıdaki butona basın.",
            "Kaydı Tamamla",
            url);

        return Ok(new
        {
            success = true,
            message = "Kayıt bağlantısı e-posta adresinize gönderildi.",
            requiresEmailConfirmation = true
        });
    }

    [HttpPost("personel/kayit")]
    [HttpPost("personel/register")]
    public async Task<IActionResult> PersonelKayit([FromBody] MobilePersonelKayitRequest request)
    {
        var temizEposta = EpostaTemizle(request.Eposta);
        var temizKullaniciAdi = request.KullaniciAdi?.Trim();
        var gorevKategori = ArizaKategorileri.Normalize(request.GorevKategori);

        if (string.IsNullOrWhiteSpace(temizKullaniciAdi) ||
            string.IsNullOrWhiteSpace(temizEposta) ||
            string.IsNullOrWhiteSpace(request.Sifre))
        {
            return BadRequest(new { success = false, message = "Kullanıcı adı, e-posta ve şifre zorunludur." });
        }

        if (!SifrelerUyusuyor(request.Sifre, request.SifreTekrar))
        {
            return BadRequest(new { success = false, message = "Şifreler birbiriyle uyuşmuyor." });
        }

        if (!ArizaKategorileri.GecerliMi(gorevKategori))
        {
            return BadRequest(new { success = false, message = "Geçerli bir görev kategorisi seçin." });
        }

        if (EpostaEngelliMi(temizEposta))
        {
            return BadRequest(new { success = false, message = "Bu e-posta adresiyle kayıt yapılamaz." });
        }

        if (await _db.Personeller.AnyAsync(x => x.KullaniciAdi == temizKullaniciAdi) ||
            await _db.Personeller.AnyAsync(x => x.Eposta == temizEposta) ||
            await _db.Vatandaslar.AnyAsync(x => x.Eposta == temizEposta))
        {
            return Conflict(new { success = false, message = "Bu kullanıcı adı veya e-posta zaten kullanılıyor." });
        }

        if (await _db.PersonelKayitIstekleri.AnyAsync(x =>
                (x.KullaniciAdi == temizKullaniciAdi || x.Eposta == temizEposta) &&
                x.Durum == PersonelKayitDurumu.Beklemede))
        {
            return Conflict(new { success = false, message = "Bu kullanıcı adı veya e-posta için bekleyen kayıt isteği var." });
        }

        var istek = new PersonelKayitIstegi
        {
            KullaniciAdi = temizKullaniciAdi,
            Eposta = temizEposta,
            Sifre = request.Sifre,
            GorevKategori = gorevKategori,
            TalepTarihi = DateTime.Now,
            Durum = PersonelKayitDurumu.Beklemede
        };

        _db.PersonelKayitIstekleri.Add(istek);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Kayıt isteğiniz admin onayına gönderildi." });
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies")]
    [HttpGet("me")]
    [HttpGet("profil")]
    public async Task<IActionResult> Profil()
    {
        var kullaniciId = AktifKullaniciId();
        var rol = User.FindFirstValue(ClaimTypes.Role) ?? "";

        if (kullaniciId == null)
        {
            return Unauthorized(new { success = false, message = "Kullanıcı doğrulanamadı." });
        }

        if (string.Equals(rol, "Personel", StringComparison.OrdinalIgnoreCase))
        {
            var personel = await _db.Personeller.AsNoTracking().FirstOrDefaultAsync(x => x.Id == kullaniciId);
            return personel == null
                ? NotFound(new { success = false, message = "Personel bulunamadı." })
                : Ok(new { success = true, role = "Personel", data = PersonelDto(personel) });
        }

        var vatandas = await _db.Vatandaslar.AsNoTracking().FirstOrDefaultAsync(x => x.Id == kullaniciId);
        return vatandas == null
            ? NotFound(new { success = false, message = "Vatandaş bulunamadı." })
            : Ok(new { success = true, role = "Vatandas", data = VatandasDto(vatandas) });
    }

    [HttpGet("arizalar/aktifler")]
    [HttpGet("aktif-arizalar")]
    [HttpGet("arizalar")]
    public async Task<IActionResult> AktifArizalar()
    {
        var arizalar = await _db.Arizalar
            .AsNoTracking()
            .Include(x => x.Vatandas)
            .Where(x => !x.CozulduMu)
            .OrderByDescending(x => x.OlusturmaTarihi)
            .Select(x => ArizaDto(x))
            .ToListAsync();

        return Ok(new { success = true, data = arizalar });
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Vatandas")]
    [HttpGet("arizalarim")]
    [HttpGet("vatandas/arizalar")]
    public async Task<IActionResult> VatandasArizalari()
    {
        var vatandasId = AktifKullaniciId();
        if (vatandasId == null)
        {
            return Unauthorized(new { success = false, message = "Kullanıcı doğrulanamadı." });
        }

        var arizalar = await _db.Arizalar
            .AsNoTracking()
            .Where(x => x.VatandasId == vatandasId)
            .OrderByDescending(x => x.OlusturmaTarihi)
            .Select(x => ArizaDto(x))
            .ToListAsync();

        return Ok(new { success = true, data = arizalar });
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Vatandas")]
    [Consumes("multipart/form-data")]
    [HttpPost("arizalar")]
    [HttpPost("ariza/ekle")]
    [HttpPost("ariza/bildir")]
    [HttpPost("arizalar/bildir")]
    [HttpPost("faults")]
    public async Task<IActionResult> ArizaBildir([FromForm] MobileArizaBildirRequest request)
    {
        var vatandasId = AktifKullaniciId();
        if (vatandasId == null)
        {
            return Unauthorized(new { success = false, message = "Kullanıcı doğrulanamadı." });
        }

        var hata = ArizaBildirimiDogrula(request);
        if (hata != null)
        {
            return BadRequest(new { success = false, message = hata });
        }

        var fotografYolu = await FotografKaydet(request.Fotograf!);
        var kategori = ArizaKategorileri.Normalize(request.Kategori);
        var ariza = new Ariza
        {
            Baslik = request.Baslik.Trim(),
            Aciklama = request.Aciklama.Trim(),
            Kategori = kategori,
            AdresTarifi = AdresTarifiOku(request),
            Enlem = EnlemOku(request),
            Boylam = BoylamOku(request),
            FotografYolu = fotografYolu,
            OlusturmaTarihi = DateTime.Now,
            GuncellenmeTarihi = DateTime.Now,
            Durum = ArizaDurumu.Beklemede,
            CozulduMu = false,
            VatandasId = vatandasId
        };

        _db.Arizalar.Add(ariza);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "Arıza bildirimi başarıyla alındı.",
            data = ArizaDto(ariza)
        });
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Personel")]
    [HttpGet("personel/arizalar")]
    [HttpGet("personel/ihbarlar")]
    public async Task<IActionResult> PersonelArizalari()
    {
        var kategori = ArizaKategorileri.Normalize(User.FindFirst("GorevKategori")?.Value);
        var arizalar = await _db.Arizalar
            .AsNoTracking()
            .Include(x => x.Vatandas)
            .Where(x => x.Kategori == kategori)
            .OrderByDescending(x => x.OlusturmaTarihi)
            .Select(x => ArizaDto(x))
            .ToListAsync();

        return Ok(new { success = true, kategori, data = arizalar });
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Admin,Personel")]
    [HttpPost("ariza/{id:int}/status")]
    [HttpPost("personel/arizalar/{id:int}/durum")]
    [HttpPut("personel/arizalar/{id:int}/durum")]
    [HttpPost("personel/durum-guncelle/{id:int}")]
    [HttpPut("personel/durum-guncelle/{id:int}")]
    public async Task<IActionResult> PersonelDurumGuncelle(int id, [FromBody] MobileDurumGuncelleRequest request)
    {
        if (!DurumOku(request, out var yeniDurum))
        {
            return BadRequest(new { success = false, message = "Geçerli bir durum gönderin." });
        }

        var arizaSorgusu = _db.Arizalar.AsQueryable();
        if (!User.IsInRole("Admin"))
        {
            var kategori = ArizaKategorileri.Normalize(User.FindFirst("GorevKategori")?.Value);
            arizaSorgusu = arizaSorgusu.Where(x => x.Kategori == kategori);
        }

        var ariza = await arizaSorgusu.FirstOrDefaultAsync(x => x.Id == id);

        if (ariza == null)
        {
            return NotFound(new { success = false, message = "Arıza bulunamadı veya yetkiniz yok." });
        }

        ariza.Durum = yeniDurum;
        ariza.GuncellenmeTarihi = DateTime.Now;

        if (yeniDurum == ArizaDurumu.IptalEdildi)
        {
            DosyayiSil(ariza.FotografYolu);
            _db.Arizalar.Remove(ariza);
        }
        else if (yeniDurum == ArizaDurumu.Tamamlandi)
        {
            ariza.CozulduMu = true;
            DosyayiSil(ariza.FotografYolu);
            ariza.FotografYolu = null;
        }
        else
        {
            ariza.CozulduMu = false;
        }

        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Durum başarıyla güncellendi." });
    }

    private async Task<IActionResult> LoginAsync(MobileLoginRequest request, string rol)
    {
        var temizEposta = EpostaTemizle(request.Eposta);
        if (string.IsNullOrWhiteSpace(temizEposta) || string.IsNullOrWhiteSpace(request.Sifre))
        {
            return BadRequest(new { success = false, message = "E-posta ve şifre zorunludur." });
        }

        if (EpostaEngelliMi(temizEposta))
        {
            return BadRequest(new { success = false, message = "Bu e-posta adresi sistem tarafından engellenmiş." });
        }

        if (string.Equals(rol, "Personel", StringComparison.OrdinalIgnoreCase))
        {
            var personel = await _db.Personeller.FirstOrDefaultAsync(x => x.Eposta == temizEposta && x.Sifre == request.Sifre);
            if (personel == null)
            {
                var bekleyenIstek = await _db.PersonelKayitIstekleri.AnyAsync(x =>
                    x.Eposta == temizEposta &&
                    x.Sifre == request.Sifre &&
                    x.Durum == PersonelKayitDurumu.Beklemede);

                return bekleyenIstek
                    ? BadRequest(new { success = false, message = "Kayıt isteğiniz henüz admin onayı bekliyor." })
                    : Unauthorized(new { success = false, message = "E-posta veya şifre hatalı." });
            }

            var claims = PersonelClaims(personel);
            var token = await OturumAcVeTokenOlustur(claims);

            return Ok(new
            {
                success = true,
                role = "Personel",
                message = "Giriş başarılı.",
                token = token.Token,
                expiresAt = token.ExpiresAt,
                data = PersonelDto(personel)
            });
        }

        var vatandas = await _db.Vatandaslar.FirstOrDefaultAsync(x => x.Eposta == temizEposta && x.Sifre == request.Sifre);
        if (vatandas == null || !vatandas.EpostaOnaylandi)
        {
            return Unauthorized(new
            {
                success = false,
                message = "E-posta veya şifre hatalı. E-posta onayınızı tamamladığınızdan emin olun."
            });
        }

        var vatandasClaims = VatandasClaims(vatandas);
        var vatandasToken = await OturumAcVeTokenOlustur(vatandasClaims);

        return Ok(new
        {
            success = true,
            role = "Vatandas",
            message = "Giriş başarılı.",
            token = vatandasToken.Token,
            expiresAt = vatandasToken.ExpiresAt,
            data = VatandasDto(vatandas)
        });
    }

    private async Task<ApiTokenResult> OturumAcVeTokenOlustur(List<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return _apiTokenService.TokenOlustur(claims);
    }

    private bool EpostaEngelliMi(string eposta)
    {
        var temizEposta = EpostaTemizle(eposta);
        return _db.EngellenenEpostalar.Any(x => x.Eposta == temizEposta);
    }

    private int? AktifKullaniciId()
    {
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }

    private static string KullaniciRolunuOku(MobileLoginRequest request)
    {
        var rol = request.Rol ?? request.Tip ?? request.UserType ?? request.AccountType ?? request.KullaniciTipi;
        if (string.IsNullOrWhiteSpace(rol))
        {
            return "Vatandas";
        }

        return rol.Trim().ToLowerInvariant() switch
        {
            "personel" or "staff" or "employee" => "Personel",
            _ => "Vatandas"
        };
    }

    private static bool SifrelerUyusuyor(string sifre, string? sifreTekrar)
    {
        return string.IsNullOrWhiteSpace(sifreTekrar) || string.Equals(sifre, sifreTekrar, StringComparison.Ordinal);
    }

    private static List<Claim> VatandasClaims(Vatandas vatandas)
    {
        return new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, vatandas.Id.ToString()),
            new(ClaimTypes.Name, vatandas.KullaniciAdi),
            new(ClaimTypes.Email, vatandas.Eposta),
            new(ClaimTypes.Role, "Vatandas")
        };
    }

    private static List<Claim> PersonelClaims(Personel personel)
    {
        var gorevKategori = ArizaKategorileri.Normalize(personel.GorevKategori);
        return new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, personel.Id.ToString()),
            new(ClaimTypes.Name, personel.KullaniciAdi),
            new(ClaimTypes.Email, personel.Eposta),
            new(ClaimTypes.Role, "Personel"),
            new("GorevKategori", gorevKategori)
        };
    }

    private static List<Claim> AdminClaims(Admin admin)
    {
        return new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new(ClaimTypes.Name, admin.KullaniciAdi),
            new(ClaimTypes.Role, "Admin")
        };
    }

    private static object VatandasDto(Vatandas vatandas)
    {
        return new
        {
            vatandas.Id,
            vatandas.KullaniciAdi,
            vatandas.Eposta,
            vatandas.EpostaOnaylandi,
            vatandas.OlusturmaTarihi
        };
    }

    private static object PersonelDto(Personel personel)
    {
        return new
        {
            personel.Id,
            personel.KullaniciAdi,
            personel.Eposta,
            GorevKategori = ArizaKategorileri.Normalize(personel.GorevKategori),
            personel.OlusturmaTarihi
        };
    }

    private static object AdminDto(Admin admin)
    {
        return new
        {
            admin.Id,
            admin.KullaniciAdi
        };
    }

    private static object ArizaDto(Ariza ariza)
    {
        return new
        {
            ariza.Id,
            ariza.Baslik,
            ariza.Aciklama,
            Kategori = ArizaKategorileri.Normalize(ariza.Kategori),
            Durum = ariza.Durum.ToString(),
            ariza.CozulduMu,
            ariza.Enlem,
            ariza.Boylam,
            ariza.AdresTarifi,
            ariza.FotografYolu,
            ariza.OlusturmaTarihi,
            ariza.GuncellenmeTarihi,
            BildirenAd = ariza.Vatandas != null ? ariza.Vatandas.KullaniciAdi : "Bilinmiyor",
            BildirenEposta = ariza.Vatandas != null ? ariza.Vatandas.Eposta : ""
        };
    }

    private static string? ArizaBildirimiDogrula(MobileArizaBildirRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Baslik))
        {
            return "Başlık zorunludur.";
        }

        if (request.Baslik.Trim().Length > 30)
        {
            return "Başlık en fazla 30 karakter olabilir.";
        }

        if (string.IsNullOrWhiteSpace(request.Aciklama))
        {
            return "Açıklama zorunludur.";
        }

        if (request.Aciklama.Trim().Length > 100)
        {
            return "Açıklama en fazla 100 karakter olabilir.";
        }

        if (!ArizaKategorileri.GecerliMi(request.Kategori))
        {
            return "Geçerli bir arıza kategorisi seçin.";
        }

        if (string.IsNullOrWhiteSpace(AdresTarifiOku(request)))
        {
            return "Adres tarifi zorunludur.";
        }

        var enlem = EnlemOku(request);
        var boylam = BoylamOku(request);
        if (enlem < 35.8 || enlem > 42.5 || boylam < 25.5 || boylam > 45.0)
        {
            return "Konum Türkiye sınırları içinde olmalıdır.";
        }

        if (request.Fotograf == null || request.Fotograf.Length == 0)
        {
            return "Arıza fotoğrafı zorunludur.";
        }

        return FotografDogrula(request.Fotograf);
    }

    private static string? FotografDogrula(IFormFile fotograf)
    {
        if (fotograf.Length > MaksimumFotografBoyutu)
        {
            return "Fotoğraf 10MB'dan büyük olamaz.";
        }

        var uzanti = Path.GetExtension(fotograf.FileName).ToLowerInvariant();
        var icerikTipi = (fotograf.ContentType ?? "").ToLowerInvariant();
        if (!IzinliFotografUzantilari.Contains(uzanti) || !IzinliFotografIcerikTipleri.Contains(icerikTipi))
        {
            return "Sadece JPG, JPEG veya PNG fotoğraf yüklenebilir.";
        }

        return null;
    }

    private async Task<string> FotografKaydet(IFormFile fotograf)
    {
        var uploadsKlasoru = Path.Combine(_env.WebRootPath, "uploads");
        if (!Directory.Exists(uploadsKlasoru))
        {
            Directory.CreateDirectory(uploadsKlasoru);
        }

        var dosyaAdi = $"{Guid.NewGuid()}{Path.GetExtension(fotograf.FileName).ToLowerInvariant()}";
        var tamYol = Path.Combine(uploadsKlasoru, dosyaAdi);

        await using var stream = new FileStream(tamYol, FileMode.Create);
        await fotograf.CopyToAsync(stream);

        return $"/uploads/{dosyaAdi}";
    }

    private void DosyayiSil(string? fotografYolu)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fotografYolu))
            {
                return;
            }

            var tamYol = Path.Combine(_env.WebRootPath, fotografYolu.TrimStart('/'));
            if (System.IO.File.Exists(tamYol))
            {
                System.IO.File.Delete(tamYol);
            }
        }
        catch
        {
            // Dosya temizliği API cevabını bozmasın.
        }
    }

    private static bool DurumOku(MobileDurumGuncelleRequest request, out ArizaDurumu durum)
    {
        if (request.DurumKodu.HasValue && Enum.IsDefined(typeof(ArizaDurumu), request.DurumKodu.Value))
        {
            durum = (ArizaDurumu)request.DurumKodu.Value;
            return true;
        }

        var metin = request.YeniDurum ?? request.Durum ?? request.Status;
        if (int.TryParse(metin, out var kod) && Enum.IsDefined(typeof(ArizaDurumu), kod))
        {
            durum = (ArizaDurumu)kod;
            return true;
        }

        return Enum.TryParse(metin, ignoreCase: true, out durum);
    }

    private static double EnlemOku(MobileArizaBildirRequest request)
    {
        return request.Enlem ?? request.Lat ?? request.Latitude ?? 0;
    }

    private static double BoylamOku(MobileArizaBildirRequest request)
    {
        return request.Boylam ?? request.Lng ?? request.Longitude ?? 0;
    }

    private static string AdresTarifiOku(MobileArizaBildirRequest request)
    {
        return (request.AdresTarifi ?? request.Adres ?? request.Address ?? "").Trim();
    }

    private static string EpostaTemizle(string? eposta)
    {
        return (eposta ?? "").Trim().ToLowerInvariant();
    }

    private static string TokenUret()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }
}

public class MobileLoginRequest
{
    public string Eposta { get; set; } = "";
    public string Sifre { get; set; } = "";
    public string? Rol { get; set; }
    public string? Tip { get; set; }
    public string? UserType { get; set; }
    public string? AccountType { get; set; }
    public string? KullaniciTipi { get; set; }
}

public class MobileAdminLoginRequest
{
    public string? KullaniciAdi { get; set; }
    public string? Eposta { get; set; }
    public string Sifre { get; set; } = "";
}

public class MobileVatandasKayitRequest
{
    public string KullaniciAdi { get; set; } = "";
    public string Eposta { get; set; } = "";
    public string Sifre { get; set; } = "";
    public string? SifreTekrar { get; set; }
}

public class MobilePersonelKayitRequest
{
    public string KullaniciAdi { get; set; } = "";
    public string Eposta { get; set; } = "";
    public string Sifre { get; set; } = "";
    public string? SifreTekrar { get; set; }
    public string GorevKategori { get; set; } = "";
}

public class MobileArizaBildirRequest
{
    public string Baslik { get; set; } = "";
    public string Aciklama { get; set; } = "";
    public string Kategori { get; set; } = "";
    public string? AdresTarifi { get; set; }
    public string? Adres { get; set; }
    public string? Address { get; set; }
    public double? Enlem { get; set; }
    public double? Boylam { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public IFormFile? Fotograf { get; set; }
}

public class MobileDurumGuncelleRequest
{
    public string? YeniDurum { get; set; }
    public string? Durum { get; set; }
    public string? Status { get; set; }
    public int? DurumKodu { get; set; }
}
