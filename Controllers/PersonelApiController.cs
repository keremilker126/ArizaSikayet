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
[Route("api/personel")]
public class PersonelApiController : ControllerBase
{
    private readonly UygulamaDbContext _db;
    private readonly EmailService _emailService;
    private readonly IWebHostEnvironment _env;
    private readonly ApiTokenService _apiTokenService;

    private static readonly string[] Kategoriler = ArizaKategorileri.TumKategoriler;

    public PersonelApiController(UygulamaDbContext db, EmailService emailService, IWebHostEnvironment env, ApiTokenService apiTokenService)
    {
        _db = db;
        _emailService = emailService;
        _env = env;
        _apiTokenService = apiTokenService;
    }

    // 1. Kategorileri Getir (Kayıt ekranında dropdown için)
    [HttpGet("kategoriler")]
    public IActionResult GetKategoriler()
    {
        return Ok(new { success = true, data = Kategoriler });
    }

    // 2. Personel Login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] PersonelLoginRequest request)
    {
        try
        {
            var temizEposta = EpostaTemizle(request.Eposta);
            var personel = _db.Personeller.FirstOrDefault(x => x.Eposta == temizEposta && x.Sifre == request.Sifre);

            if (EpostaEngelliMi(temizEposta))
            {
                return BadRequest(new { success = false, message = "Bu e-posta adresi sistem tarafından engellenmiş." });
            }

            if (personel == null)
            {
                var bekleyenIstek = _db.PersonelKayitIstekleri.Any(x =>
                    x.Eposta == temizEposta && x.Sifre == request.Sifre && x.Durum == PersonelKayitDurumu.Beklemede);

                if (bekleyenIstek)
                {
                    return BadRequest(new { success = false, message = "Kayıt isteğiniz henüz admin onayı bekliyor." });
                }

                return Unauthorized(new { success = false, message = "E-posta veya şifre hatalı." });
            }

            // Flutter tarafında JWT kullanman en doğrusudur. Cookie bazlı devam ediyorsan aşağıdaki yapı kalabilir.
            var gorevKategori = ArizaKategorileri.Normalize(personel.GorevKategori);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, personel.Id.ToString()),
                new Claim(ClaimTypes.Name, personel.KullaniciAdi),
                new Claim(ClaimTypes.Role, "Personel"),
                new Claim("GorevKategori", gorevKategori),
                new Claim(ClaimTypes.Email, personel.Eposta)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            var token = _apiTokenService.TokenOlustur(claims);

            return Ok(new
            {
                success = true,
                message = "Giriş başarılı.",
                token = token.Token,
                expiresAt = token.ExpiresAt,
                data = new
                {
                    personel.Id,
                    personel.KullaniciAdi,
                    personel.Eposta,
                    GorevKategori = gorevKategori
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Giriş sırasında bir hata oluştu.", details = ex.Message });
        }
    }

    // 3. Personel Kayıt İstediği
    [HttpPost("kayit")]
    public IActionResult Kayit([FromBody] PersonelKayitRequest request)
    {
        try
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

            var gorevKategori = ArizaKategorileri.Normalize(request.GorevKategori);

            if (!ArizaKategorileri.GecerliMi(gorevKategori))
            {
                return BadRequest(new { success = false, message = "Geçerli bir görev kategorisi seçin." });
            }

            if (EpostaEngelliMi(temizEposta))
            {
                return BadRequest(new { success = false, message = "Bu e-posta adresiyle kayıt yapılamaz." });
            }

            if (_db.Personeller.Any(x => x.KullaniciAdi == temizKullaniciAdi))
            {
                return Conflict(new { success = false, message = "Bu kullanıcı adı zaten kullanılıyor." });
            }

            if (_db.Personeller.Any(x => x.Eposta == temizEposta) || _db.Vatandaslar.Any(x => x.Eposta == temizEposta))
            {
                return Conflict(new { success = false, message = "Bu e-posta adresiyle kayıtlı bir hesap var." });
            }

            if (_db.PersonelKayitIstekleri.Any(x => (x.KullaniciAdi == temizKullaniciAdi || x.Eposta == temizEposta) && x.Durum == PersonelKayitDurumu.Beklemede))
            {
                return Conflict(new { success = false, message = "Bu kullanıcı adı veya e-posta için bekleyen bir kayıt isteği var." });
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
            _db.SaveChanges();

            return Ok(new { success = true, message = "Kayıt isteğiniz admin onayına gönderildi." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Kayıt sırasında bir hata oluştu.", details = ex.Message });
        }
    }

    // 4. Admin İşlemi: Kayıt İstediği Onayla
    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Admin")]
    [HttpPost("kayit-istegi-onayla/{id}")]
    public async Task<IActionResult> KayitIstegiOnayla(int id)
    {
        try
        {
            var istek = _db.PersonelKayitIstekleri.Find(id);
            if (istek == null || istek.Durum != PersonelKayitDurumu.Beklemede)
            {
                return NotFound(new { success = false, message = "Geçerli bir istek bulunamadı." });
            }

            if (_db.Personeller.Any(x => x.KullaniciAdi == istek.KullaniciAdi || x.Eposta == istek.Eposta) ||
                _db.Vatandaslar.Any(x => x.Eposta == istek.Eposta) || EpostaEngelliMi(istek.Eposta))
            {
                istek.Durum = PersonelKayitDurumu.Reddedildi;
                istek.SonucTarihi = DateTime.Now;
                _db.SaveChanges();
                return BadRequest(new { success = false, message = "Güvenlik/çakışma sebebiyle istek otomatik reddedildi." });
            }

            istek.Durum = PersonelKayitDurumu.Onaylandi;
            istek.SonucTarihi = DateTime.Now;
            istek.KayitTamamlamaToken = TokenUret();
            _db.SaveChanges();

            // Web projesindeki linki API üzerinden simüle etmeniz gerekebilir, domaini statik veya config'den çekin.
            var url = Url.Action("KayitTamamla", "Personel", new { token = istek.KayitTamamlamaToken }, Request.Scheme)!;
            await _emailService.GonderAsync(istek.Eposta, "Personel Kaydı", "Kayıt Onaylandı", "Kaydınızı tamamlamak için butona tıklayın.", "Kaydı Tamamla", url);

            return Ok(new { success = true, message = "İstek onaylandı ve e-posta gönderildi." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Onaylama sırasında bir hata oluştu.", details = ex.Message });
        }
    }

    // 5. Admin İşlemi: Kayıt İsteği Reddet
    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Admin")]
    [HttpPost("kayit-istegi-reddet/{id}")]
    public IActionResult KayitIstegiReddet(int id)
    {
        try
        {
            var istek = _db.PersonelKayitIstekleri.Find(id);
            if (istek != null && istek.Durum == PersonelKayitDurumu.Beklemede)
            {
                istek.Durum = PersonelKayitDurumu.Reddedildi;
                istek.SonucTarihi = DateTime.Now;
                _db.SaveChanges();
                return Ok(new { success = true, message = "İstek reddedildi." });
            }
            return NotFound(new { success = false, message = "İstek bulunamadı veya zaten sonuçlanmış." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Reddetme sırasında bir hata oluştu.", details = ex.Message });
        }
    }

    // 6. Token ile Kayıt Tamamla (E-postadan gelen link Flutter'a düşer, Flutter bu API'ye istek atar)
    [HttpPost("kayit-tamamla/{token}")]
    public IActionResult KayitTamamla(string token)
    {
        try
        {
            var istek = _db.PersonelKayitIstekleri.FirstOrDefault(x => x.KayitTamamlamaToken == token);
            if (istek == null || istek.Durum != PersonelKayitDurumu.Onaylandi || istek.KayitTamamlamaTokenKullanildi)
            {
                return BadRequest(new { success = false, message = "Kayıt bağlantısı geçersiz veya daha önce kullanılmış." });
            }

            if (_db.Personeller.Any(x => x.KullaniciAdi == istek.KullaniciAdi || x.Eposta == istek.Eposta) || EpostaEngelliMi(istek.Eposta))
            {
                return BadRequest(new { success = false, message = "Çakışma sebebiyle hesap oluşturulamadı." });
            }

            var personel = new Personel
            {
                KullaniciAdi = istek.KullaniciAdi,
                Eposta = istek.Eposta,
                Sifre = istek.Sifre,
                GorevKategori = ArizaKategorileri.Normalize(istek.GorevKategori),
                OlusturmaTarihi = DateTime.Now
            };

            _db.Personeller.Add(personel);
            istek.KayitTamamlamaTokenKullanildi = true;
            _db.SaveChanges();

            return Ok(new { success = true, message = "Kaydınız tamamlandı. Giriş yapabilirsiniz." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Kayıt tamamlanırken bir hata oluştu.", details = ex.Message });
        }
    }

    // 7. Personel Paneli (Kendisine atanan arızaları listeleme)
    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Personel")]
    [HttpGet("arizalar")]
    public IActionResult Panel()
    {
        try
        {
            var kategori = ArizaKategorileri.Normalize(User.FindFirst("GorevKategori")?.Value);
            var arizalar = _db.Arizalar
                .Include(x => x.Vatandas)
                .Where(x => x.Kategori == kategori)
                .OrderByDescending(x => x.OlusturmaTarihi)
                .Select(x => new
                {
                    x.Id,
                    x.Baslik,
                    x.Aciklama,
                    x.Kategori,
                    Durum = x.Durum.ToString(),
                    x.CozulduMu,
                    x.Enlem,
                    x.Boylam,
                    x.AdresTarifi,
                    x.FotografYolu,
                    x.OlusturmaTarihi,
                    x.GuncellenmeTarihi,
                    BildirenAd = x.Vatandas != null ? x.Vatandas.KullaniciAdi : "Bilinmiyor",
                    BildirenEposta = x.Vatandas != null ? x.Vatandas.Eposta : ""
                })
                .ToList();

            return Ok(new { success = true, kategori = kategori, data = arizalar });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Arızalar listelenirken bir hata oluştu.", details = ex.Message });
        }
    }

    // 8. Personel: Durum Güncelle
    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Personel")]
    [HttpPut("durum-guncelle/{id}")]
    public IActionResult DurumGuncelle(int id, [FromBody] PersonelDurumGuncelleRequest request)
    {
        try
        {
            var kategori = ArizaKategorileri.Normalize(User.FindFirst("GorevKategori")?.Value);
            var ariza = _db.Arizalar.FirstOrDefault(x => x.Id == id && x.Kategori == kategori);

            if (ariza == null)
            {
                return NotFound(new { success = false, message = "Arıza bulunamadı veya yetkiniz yok." });
            }

            ariza.Durum = request.YeniDurum;
            ariza.GuncellenmeTarihi = DateTime.Now;

            if (request.YeniDurum == ArizaDurumu.IptalEdildi)
            {
                DosyayiSil(ariza.FotografYolu);
                _db.Arizalar.Remove(ariza);
            }
            else if (request.YeniDurum == ArizaDurumu.Tamamlandi)
            {
                ariza.CozulduMu = true;
                DosyayiSil(ariza.FotografYolu);
                ariza.FotografYolu = null;
            }
            else
            {
                ariza.CozulduMu = false;
            }

            _db.SaveChanges();
            return Ok(new { success = true, message = "Durum başarıyla güncellendi." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Durum güncellenirken bir hata oluştu.", details = ex.Message });
        }
    }

    // 9. Personel: Şifre Değiştir
    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Personel")]
    [HttpPost("sifre-degistir")]
    public IActionResult SifreDegistir([FromBody] PersonelSifreDegistirRequest request)
    {
        try
        {
            var personelIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(personelIdText, out var personelId))
            {
                return Unauthorized(new { success = false, message = "Kullanıcı doğrulanamadı." });
            }

            var personel = _db.Personeller.Find(personelId);
            if (personel == null || personel.Sifre != request.MevcutSifre)
            {
                return BadRequest(new { success = false, message = "Mevcut şifreniz hatalı." });
            }

            if (request.YeniSifre != request.YeniSifreTekrar)
            {
                return BadRequest(new { success = false, message = "Yeni şifreler uyuşmuyor." });
            }

            personel.Sifre = request.YeniSifre;
            _db.SaveChanges();

            return Ok(new { success = true, message = "Şifreniz başarıyla güncellendi." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Şifre değiştirilirken bir hata oluştu.", details = ex.Message });
        }
    }

    // 10. Şifremi Unuttum (E-posta gönderimi)
    [HttpPost("sifremi-unuttum")]
    public async Task<IActionResult> SifremiUnuttum([FromBody] SifreUnuttumRequest request)
    {
        try
        {
            var temizEposta = EpostaTemizle(request.Eposta);
            var personel = _db.Personeller.FirstOrDefault(x => x.Eposta == temizEposta);

            if (personel != null && !EpostaEngelliMi(temizEposta))
            {
                personel.SifreSifirlamaToken = TokenUret();
                personel.SifreSifirlamaTokenTarihi = DateTime.Now;
                _db.SaveChanges();

                var url = Url.Action("SifreSifirla", "Personel", new { token = personel.SifreSifirlamaToken }, Request.Scheme)!;
                await _emailService.GonderAsync(temizEposta, "Şifre Sıfırlama", "Şifrenizi Sıfırlayın", "Yeni şifre için butona tıklayın.", "Şifreyi Sıfırla", url);
            }

            // Güvenlik gereği kullanıcı var olmasa da aynı mesajı dönüyoruz (Email enumeration attack önlemi)
            return Ok(new { success = true, message = "E-posta kayıtlıysa şifre sıfırlama bağlantısı gönderildi." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "İşlem sırasında bir hata oluştu.", details = ex.Message });
        }
    }

    // 11. Şifre Sıfırla (Yeni şifre belirleme)
    [HttpPost("sifre-sifirla")]
    public IActionResult SifreSifirla([FromBody] SifreSifirlaRequest request)
    {
        try
        {
            var personel = _db.Personeller.FirstOrDefault(x => x.SifreSifirlamaToken == request.Token);
            if (personel == null || personel.SifreSifirlamaTokenTarihi < DateTime.Now.AddHours(-2))
            {
                return BadRequest(new { success = false, message = "Bağlantı geçersiz veya süresi dolmuş." });
            }

            if (request.YeniSifre != request.YeniSifreTekrar)
            {
                return BadRequest(new { success = false, message = "Şifreler uyuşmuyor." });
            }

            personel.Sifre = request.YeniSifre;
            personel.SifreSifirlamaToken = null;
            personel.SifreSifirlamaTokenTarihi = null;
            _db.SaveChanges();

            return Ok(new { success = true, message = "Şifreniz güncellendi. Yeni şifrenizle giriş yapabilirsiniz." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Şifre sıfırlanırken bir hata oluştu.", details = ex.Message });
        }
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new { success = true, message = "Başarıyla çıkış yapıldı." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Çıkış yapılırken bir hata oluştu.", details = ex.Message });
        }
    }

    // --- YARDIMCI METOTLAR ---
    private bool EpostaEngelliMi(string eposta)
    {
        var temizEposta = EpostaTemizle(eposta);
        return _db.EngellenenEpostalar.Any(x => x.Eposta == temizEposta);
    }

    private static string EpostaTemizle(string? eposta)
    {
        return (eposta ?? "").Trim().ToLowerInvariant();
    }

    private static string TokenUret()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }

    private void DosyayiSil(string? fotografYolu)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fotografYolu)) return;

            var tamYol = Path.Combine(_env.WebRootPath, fotografYolu.TrimStart('/'));

            if (System.IO.File.Exists(tamYol))
            {
                System.IO.File.Delete(tamYol);
            }
        }
        catch
        {
            // API akışını bozmamak için dosyası silinememe hatasını yutuyoruz
        }
    }
}

// --- FLUTTER İÇİN GEREKLİ DTO MODELLERİ ---

public class PersonelLoginRequest
{
    public string Eposta { get; set; } = null!;
    public string Sifre { get; set; } = null!;
}

public class PersonelKayitRequest
{
    public string KullaniciAdi { get; set; } = null!;
    public string Eposta { get; set; } = null!;
    public string Sifre { get; set; } = null!;
    public string SifreTekrar { get; set; } = null!;
    public string GorevKategori { get; set; } = null!;
}

public class PersonelSifreDegistirRequest
{
    public string MevcutSifre { get; set; } = null!;
    public string YeniSifre { get; set; } = null!;
    public string YeniSifreTekrar { get; set; } = null!;
}

public class PersonelDurumGuncelleRequest
{
    public ArizaDurumu YeniDurum { get; set; }
}

public class SifreUnuttumRequest
{
    public string Eposta { get; set; } = null!;
}

public class SifreSifirlaRequest
{
    public string Token { get; set; } = null!;
    public string YeniSifre { get; set; } = null!;
    public string YeniSifreTekrar { get; set; } = null!;
}
