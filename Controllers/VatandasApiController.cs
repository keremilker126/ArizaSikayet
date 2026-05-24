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
[Route("api/vatandas")]
public class VatandasApiController : ControllerBase
{
    private readonly UygulamaDbContext _db;
    private readonly EmailService _emailService;
    private readonly ApiTokenService _apiTokenService;
    private const long MaksimumFotografBoyutu = 10 * 1024 * 1024;
    private static readonly string[] IzinliFotografUzantilari = { ".jpg", ".jpeg", ".png" };
    private static readonly string[] IzinliFotografIcerikTipleri = { "image/jpeg", "image/png" };

    public VatandasApiController(UygulamaDbContext db, EmailService emailService, ApiTokenService apiTokenService)
    {
        _db = db;
        _emailService = emailService;
        _apiTokenService = apiTokenService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] VatandasLoginRequest request)
    {
        var temizEposta = EpostaTemizle(request.Eposta);
        if (EpostaEngelliMi(temizEposta))
        {
            return BadRequest(new { success = false, message = "Bu e-posta adresi sistem tarafından engellenmiş." });
        }

        var vatandas = await _db.Vatandaslar.FirstOrDefaultAsync(x => x.Eposta == temizEposta && x.Sifre == request.Sifre);
        if (vatandas == null || !vatandas.EpostaOnaylandi)
        {
            return Unauthorized(new { success = false, message = "E-posta veya şifre hatalı. E-posta onayınızı tamamladığınızdan emin olun." });
        }

        var claims = VatandasClaims(vatandas);
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        var token = _apiTokenService.TokenOlustur(claims);
        return Ok(new
        {
            success = true,
            message = "Giriş başarılı.",
            token = token.Token,
            expiresAt = token.ExpiresAt,
            data = VatandasDto(vatandas)
        });
    }

    [HttpPost("kayit")]
    public async Task<IActionResult> Kayit([FromBody] VatandasKayitRequest request)
    {
        var temizEposta = EpostaTemizle(request.Eposta);
        var temizKullaniciAdi = request.KullaniciAdi?.Trim();

        if (string.IsNullOrWhiteSpace(temizKullaniciAdi) || string.IsNullOrWhiteSpace(temizEposta) || string.IsNullOrWhiteSpace(request.Sifre))
        {
            return BadRequest(new { success = false, message = "Kullanıcı adı, e-posta ve şifre zorunludur." });
        }

        if (request.Sifre != request.SifreTekrar)
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
        await _emailService.GonderAsync(temizEposta, "Vatandaş kaydınızı tamamlayın", "Kaydınızı tamamlayın", "Arıza Şikayet hesabınızı etkinleştirmek için aşağıdaki butona basın.", "Kaydı Tamamla", url);

        return Ok(new { success = true, message = "Kayıt bağlantısı e-posta adresinize gönderildi." });
    }

    [HttpPost("kayit-tamamla/{token}")]
    public async Task<IActionResult> KayitTamamla(string token)
    {
        var vatandas = await _db.Vatandaslar.FirstOrDefaultAsync(x => x.KayitOnayToken == token);
        if (vatandas == null || vatandas.KayitOnayTokenKullanildi)
        {
            return BadRequest(new { success = false, message = "Kayıt bağlantısı geçersiz veya daha önce kullanılmış." });
        }

        if (EpostaEngelliMi(vatandas.Eposta))
        {
            return BadRequest(new { success = false, message = "Bu e-posta adresi engellendiği için kayıt tamamlanamadı." });
        }

        vatandas.EpostaOnaylandi = true;
        vatandas.KayitOnayTokenKullanildi = true;
        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Kaydınız tamamlandı. Giriş yapabilirsiniz." });
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Vatandas")]
    [HttpGet("profil")]
    public async Task<IActionResult> Profil()
    {
        var vatandasId = AktifKullaniciId();
        if (vatandasId == null)
        {
            return Unauthorized(new { success = false, message = "Kullanıcı doğrulanamadı." });
        }

        var vatandas = await _db.Vatandaslar.AsNoTracking().FirstOrDefaultAsync(x => x.Id == vatandasId);
        if (vatandas == null)
        {
            return NotFound(new { success = false, message = "Vatandaş bulunamadı." });
        }

        return Ok(new { success = true, data = VatandasDto(vatandas) });
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Vatandas")]
    [HttpGet("arizalar")]
    public async Task<IActionResult> Arizalarim()
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
            .Select(x => new
            {
                x.Id,
                x.Baslik,
                x.Aciklama,
                x.Kategori,
                durum = x.Durum.ToString(),
                x.CozulduMu,
                x.Enlem,
                x.Boylam,
                x.AdresTarifi,
                x.FotografYolu,
                x.OlusturmaTarihi,
                x.GuncellenmeTarihi
            })
            .ToListAsync();

        return Ok(new { success = true, data = arizalar });
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Vatandas")]
    [HttpPut("arizalar/{id:int}")]
    public async Task<IActionResult> ArizaGuncelle(int id, [FromForm] VatandasArizaGuncelleRequest request)
    {
        try
        {
            var vatandasId = AktifKullaniciId();
            if (vatandasId == null)
            {
                return Unauthorized(new { success = false, message = "Kullanıcı doğrulanamadı." });
            }

            var ariza = await _db.Arizalar.FirstOrDefaultAsync(x => x.Id == id && x.VatandasId == vatandasId);
            if (ariza == null)
            {
                return NotFound(new { success = false, message = "Arıza bulunamadı." });
            }

            if (ariza.CozulduMu || ariza.Durum == ArizaDurumu.Tamamlandi || ariza.Durum == ArizaDurumu.IptalEdildi)
            {
                return BadRequest(new { success = false, message = "Tamamlanmış veya iptal edilmiş arızalar güncellenemez." });
            }

            if (string.IsNullOrWhiteSpace(request.Baslik) || string.IsNullOrWhiteSpace(request.Aciklama) ||
                string.IsNullOrWhiteSpace(request.Kategori) || string.IsNullOrWhiteSpace(request.AdresTarifi))
            {
                return BadRequest(new { success = false, message = "Başlık, açıklama, kategori ve adres alanları zorunludur." });
            }

            ariza.Baslik = request.Baslik.Trim();
            ariza.Aciklama = request.Aciklama.Trim();
            ariza.Kategori = ArizaKategorileri.Normalize(request.Kategori);
            ariza.AdresTarifi = request.AdresTarifi.Trim();
            ariza.GuncellenmeTarihi = DateTime.Now;

            if (request.Fotograf != null && request.Fotograf.Length > 0)
            {
                var fotografHatasi = FotografDogrula(request.Fotograf);
                if (fotografHatasi != null)
                {
                    return BadRequest(new { success = false, message = fotografHatasi });
                }

                var uzanti = Path.GetExtension(request.Fotograf.FileName).ToLowerInvariant();

                DosyayiSil(ariza.FotografYolu);
                var uploadsKlasoru = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                if (!Directory.Exists(uploadsKlasoru))
                {
                    Directory.CreateDirectory(uploadsKlasoru);
                }

                var dosyaAdi = $"{Guid.NewGuid()}{uzanti}";
                var tamYol = Path.Combine(uploadsKlasoru, dosyaAdi);
                await using var stream = new FileStream(tamYol, FileMode.Create);
                await request.Fotograf.CopyToAsync(stream);
                ariza.FotografYolu = $"/uploads/{dosyaAdi}";
            }

            await _db.SaveChangesAsync();
            return Ok(new { success = true, message = "Arıza bildiriminiz güncellendi." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Arıza güncellenirken bir hata oluştu.", details = ex.Message });
        }
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Vatandas")]
    [HttpDelete("arizalar/{id:int}")]
    public async Task<IActionResult> ArizaSil(int id)
    {
        try
        {
            var vatandasId = AktifKullaniciId();
            if (vatandasId == null)
            {
                return Unauthorized(new { success = false, message = "Kullanıcı doğrulanamadı." });
            }

            var ariza = await _db.Arizalar.FirstOrDefaultAsync(x => x.Id == id && x.VatandasId == vatandasId);
            if (ariza == null)
            {
                return NotFound(new { success = false, message = "Arıza bulunamadı." });
            }

            DosyayiSil(ariza.FotografYolu);
            _db.Arizalar.Remove(ariza);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, message = "Arıza bildiriminiz silindi." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Arıza silinirken bir hata oluştu.", details = ex.Message });
        }
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Vatandas")]
    [HttpPost("sifre-degistir")]
    public async Task<IActionResult> SifreDegistir([FromBody] VatandasSifreDegistirRequest request)
    {
        var vatandasId = AktifKullaniciId();
        if (vatandasId == null)
        {
            return Unauthorized(new { success = false, message = "Kullanıcı doğrulanamadı." });
        }

        var vatandas = await _db.Vatandaslar.FindAsync(vatandasId.Value);
        if (vatandas == null || vatandas.Sifre != request.MevcutSifre)
        {
            return BadRequest(new { success = false, message = "Mevcut şifreniz hatalı." });
        }

        if (request.YeniSifre != request.YeniSifreTekrar)
        {
            return BadRequest(new { success = false, message = "Yeni şifreler uyuşmuyor." });
        }

        vatandas.Sifre = request.YeniSifre;
        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Şifreniz başarıyla güncellendi." });
    }

    [HttpPost("sifremi-unuttum")]
    public async Task<IActionResult> SifremiUnuttum([FromBody] SifreUnuttumRequest request)
    {
        var temizEposta = EpostaTemizle(request.Eposta);
        var vatandas = await _db.Vatandaslar.FirstOrDefaultAsync(x => x.Eposta == temizEposta && x.EpostaOnaylandi);

        if (vatandas != null && !EpostaEngelliMi(temizEposta))
        {
            vatandas.SifreSifirlamaToken = TokenUret();
            vatandas.SifreSifirlamaTokenTarihi = DateTime.Now;
            await _db.SaveChangesAsync();

            var url = Url.Action("SifreSifirla", "Vatandas", new { token = vatandas.SifreSifirlamaToken }, Request.Scheme)!;
            await _emailService.GonderAsync(temizEposta, "Şifre sıfırlama", "Şifrenizi sıfırlayın", "Yeni şifre belirlemek için aşağıdaki butona basın.", "Şifreyi Sıfırla", url);
        }

        return Ok(new { success = true, message = "E-posta kayıtlıysa şifre sıfırlama bağlantısı gönderildi." });
    }

    [HttpPost("sifre-sifirla")]
    public async Task<IActionResult> SifreSifirla([FromBody] SifreSifirlaRequest request)
    {
        var vatandas = await _db.Vatandaslar.FirstOrDefaultAsync(x => x.SifreSifirlamaToken == request.Token);
        if (vatandas == null || vatandas.SifreSifirlamaTokenTarihi < DateTime.Now.AddHours(-2))
        {
            return BadRequest(new { success = false, message = "Şifre sıfırlama bağlantısı geçersiz veya süresi dolmuş." });
        }

        if (request.YeniSifre != request.YeniSifreTekrar)
        {
            return BadRequest(new { success = false, message = "Şifreler uyuşmuyor." });
        }

        vatandas.Sifre = request.YeniSifre;
        vatandas.SifreSifirlamaToken = null;
        vatandas.SifreSifirlamaTokenTarihi = null;
        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Şifreniz güncellendi. Yeni şifrenizle giriş yapabilirsiniz." });
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { success = true, message = "Başarıyla çıkış yapıldı." });
    }

    private int? AktifKullaniciId()
    {
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }

    private bool EpostaEngelliMi(string eposta)
    {
        var temizEposta = EpostaTemizle(eposta);
        return _db.EngellenenEpostalar.Any(x => x.Eposta == temizEposta);
    }

    private void DosyayiSil(string? fotografYolu)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fotografYolu)) return;

            var tamYol = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", fotografYolu.TrimStart('/'));
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

    private static string EpostaTemizle(string? eposta)
    {
        return (eposta ?? "").Trim().ToLowerInvariant();
    }

    private static string TokenUret()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }

    private static string? FotografDogrula(IFormFile fotograf)
    {
        if (fotograf.Length <= 0)
        {
            return "Fotoğraf dosyası boş olamaz.";
        }

        if (fotograf.Length > MaksimumFotografBoyutu)
        {
            return "Fotoğraf 10MB'dan büyük olamaz.";
        }

        var uzanti = Path.GetExtension(fotograf.FileName).ToLowerInvariant();
        if (!IzinliFotografUzantilari.Contains(uzanti) ||
            !IzinliFotografIcerikTipleri.Contains(fotograf.ContentType.ToLowerInvariant()))
        {
            return "Sadece JPG, JPEG veya PNG fotoğraf yüklenebilir. Video ve diğer dosya türleri kabul edilmez.";
        }

        return null;
    }
}

public class VatandasLoginRequest
{
    public string Eposta { get; set; } = "";
    public string Sifre { get; set; } = "";
}

public class VatandasKayitRequest
{
    public string KullaniciAdi { get; set; } = "";
    public string Eposta { get; set; } = "";
    public string Sifre { get; set; } = "";
    public string SifreTekrar { get; set; } = "";
}

public class VatandasSifreDegistirRequest
{
    public string MevcutSifre { get; set; } = "";
    public string YeniSifre { get; set; } = "";
    public string YeniSifreTekrar { get; set; } = "";
}

public class VatandasArizaGuncelleRequest
{
    public string Baslik { get; set; } = "";
    public string Aciklama { get; set; } = "";
    public string Kategori { get; set; } = "";
    public string AdresTarifi { get; set; } = "";
    public IFormFile? Fotograf { get; set; }
}
