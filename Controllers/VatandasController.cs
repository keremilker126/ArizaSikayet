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

namespace ArizaSikayet.Controllers;

public class VatandasController : Controller
{
    private readonly UygulamaDbContext _db;
    private readonly EmailService _emailService;
    private const long MaksimumFotografBoyutu = 10 * 1024 * 1024;
    private static readonly string[] IzinliFotografUzantilari = { ".jpg", ".jpeg", ".png" };
    private static readonly string[] IzinliFotografIcerikTipleri = { "image/jpeg", "image/png" };

    public VatandasController(UygulamaDbContext db, EmailService emailService)
    {
        _db = db;
        _emailService = emailService;
    }

    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true && User.IsInRole("Vatandas"))
        {
            return RedirectToAction("Panel");
        }

        GuvenlikKoduHazirla("VatandasLoginKod");
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string eposta, string sifre, string guvenlikKodu)
    {
        if (!GuvenlikKoduDogruMu("VatandasLoginKod", guvenlikKodu))
        {
            ViewBag.Hata = "Güvenlik kodu hatalı.";
            GuvenlikKoduHazirla("VatandasLoginKod");
            return View();
        }

        var temizEposta = EpostaTemizle(eposta);
        if (EpostaEngelliMi(temizEposta))
        {
            ViewBag.Hata = "Bu e-posta adresi sistem tarafından engellenmiş.";
            GuvenlikKoduHazirla("VatandasLoginKod");
            return View();
        }

        var vatandas = await _db.Vatandaslar.FirstOrDefaultAsync(x => x.Eposta == temizEposta && x.Sifre == sifre);
        if (vatandas == null || !vatandas.EpostaOnaylandi)
        {
            ViewBag.Hata = "E-posta veya şifre hatalı. E-posta onayınızı tamamladığınızdan emin olun.";
            GuvenlikKoduHazirla("VatandasLoginKod");
            return View();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, vatandas.Id.ToString()),
            new(ClaimTypes.Name, vatandas.KullaniciAdi),
            new(ClaimTypes.Email, vatandas.Eposta),
            new(ClaimTypes.Role, "Vatandas")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return RedirectToAction("Panel");
    }

    [HttpGet]
    public IActionResult Kayit()
    {
        GuvenlikKoduHazirla("VatandasKayitKod");
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Kayit(string kullaniciAdi, string eposta, string sifre, string sifreTekrar, string guvenlikKodu)
    {
        if (!GuvenlikKoduDogruMu("VatandasKayitKod", guvenlikKodu))
        {
            ViewBag.Hata = "Güvenlik kodu hatalı.";
            GuvenlikKoduHazirla("VatandasKayitKod");
            return View();
        }

        var temizEposta = EpostaTemizle(eposta);
        if (string.IsNullOrWhiteSpace(kullaniciAdi) || string.IsNullOrWhiteSpace(temizEposta) || string.IsNullOrWhiteSpace(sifre))
        {
            ViewBag.Hata = "Tüm alanları doldurun.";
            GuvenlikKoduHazirla("VatandasKayitKod");
            return View();
        }

        if (EpostaEngelliMi(temizEposta))
        {
            ViewBag.Hata = "Bu e-posta adresiyle kayıt yapılamaz.";
            GuvenlikKoduHazirla("VatandasKayitKod");
            return View();
        }

        if (sifre != sifreTekrar)
        {
            ViewBag.Hata = "Şifreler birbiriyle uyuşmuyor.";
            GuvenlikKoduHazirla("VatandasKayitKod");
            return View();
        }

        if (await _db.Vatandaslar.AnyAsync(x => x.Eposta == temizEposta) ||
            await _db.Personeller.AnyAsync(x => x.Eposta == temizEposta))
        {
            ViewBag.Hata = "Bu e-posta adresiyle kayıtlı bir hesap var.";
            GuvenlikKoduHazirla("VatandasKayitKod");
            return View();
        }

        var token = TokenUret();
        var vatandas = new Vatandas
        {
            KullaniciAdi = kullaniciAdi.Trim(),
            Eposta = temizEposta,
            Sifre = sifre,
            KayitOnayToken = token,
            EpostaOnaylandi = false
        };

        _db.Vatandaslar.Add(vatandas);
        await _db.SaveChangesAsync();

        var url = Url.Action("KayitTamamla", "Vatandas", new { token }, Request.Scheme)!;
        await _emailService.GonderAsync(temizEposta, "Vatandaş kaydınızı tamamlayın", "Kaydınızı tamamlayın", "Arıza Şikayet hesabınızı etkinleştirmek için aşağıdaki butona basın.", "Kaydı Tamamla", url);

        ViewBag.Mesaj = "Kayıt bağlantısı e-posta adresinize gönderildi. Butona bastığınızda hesabınız açılacak.";
        GuvenlikKoduHazirla("VatandasKayitKod");
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> KayitTamamla(string token)
    {
        var vatandas = await _db.Vatandaslar.FirstOrDefaultAsync(x => x.KayitOnayToken == token);
        if (vatandas == null || vatandas.KayitOnayTokenKullanildi)
        {
            TempData["VatandasMesaj"] = "Kayıt bağlantısı geçersiz veya daha önce kullanılmış.";
            return RedirectToAction("Login");
        }

        if (EpostaEngelliMi(vatandas.Eposta))
        {
            TempData["VatandasMesaj"] = "Bu e-posta adresi engellendiği için kayıt tamamlanamadı.";
            return RedirectToAction("Login");
        }

        vatandas.EpostaOnaylandi = true;
        vatandas.KayitOnayTokenKullanildi = true;
        await _db.SaveChangesAsync();

        TempData["VatandasMesaj"] = "Kaydınız tamamlandı. Şimdi giriş yapabilirsiniz.";
        return RedirectToAction("Login");
    }

    [Authorize(Roles = "Vatandas")]
    public async Task<IActionResult> Panel()
    {
        var vatandasId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var arizalar = await _db.Arizalar
            .AsNoTracking()
            .Where(x => x.VatandasId == vatandasId)
            .OrderByDescending(x => x.OlusturmaTarihi)
            .ToListAsync();

        return View(arizalar);
    }

    [Authorize(Roles = "Vatandas")]
    [HttpPost]
    public async Task<IActionResult> ArizaGuncelle(int id, string baslik, string aciklama, string kategori, string adresTarifi, IFormFile? fotograf)
    {
        try
        {
            var vatandasId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var ariza = await _db.Arizalar.FirstOrDefaultAsync(x => x.Id == id && x.VatandasId == vatandasId);

            if (ariza == null)
            {
                TempData["Hata"] = "Güncellenecek arıza bulunamadı.";
                return RedirectToAction("Panel");
            }

            if (ariza.CozulduMu || ariza.Durum == ArizaDurumu.Tamamlandi || ariza.Durum == ArizaDurumu.IptalEdildi)
            {
                TempData["Hata"] = "Tamamlanmış veya iptal edilmiş arızalar güncellenemez.";
                return RedirectToAction("Panel");
            }

            if (string.IsNullOrWhiteSpace(baslik) || string.IsNullOrWhiteSpace(aciklama) ||
                string.IsNullOrWhiteSpace(kategori) || string.IsNullOrWhiteSpace(adresTarifi))
            {
                TempData["Hata"] = "Başlık, açıklama, kategori ve adres alanları zorunludur.";
                return RedirectToAction("Panel");
            }

            if (baslik.Trim().Length > 30 || aciklama.Trim().Length > 100)
            {
                TempData["Hata"] = "Başlık en fazla 30, açıklama en fazla 100 karakter olabilir.";
                return RedirectToAction("Panel");
            }

            ariza.Baslik = baslik.Trim();
            ariza.Aciklama = aciklama.Trim();
            ariza.Kategori = ArizaKategorileri.Normalize(kategori);
            ariza.AdresTarifi = adresTarifi.Trim();
            ariza.GuncellenmeTarihi = DateTime.Now;

            if (fotograf != null && fotograf.Length > 0)
            {
                var fotografHatasi = FotografDogrula(fotograf);
                if (fotografHatasi != null)
                {
                    TempData["Hata"] = fotografHatasi;
                    return RedirectToAction("Panel");
                }

                var uzanti = Path.GetExtension(fotograf.FileName).ToLowerInvariant();

                DosyayiSil(ariza.FotografYolu);

                var uploadsKlasoru = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                if (!Directory.Exists(uploadsKlasoru))
                {
                    Directory.CreateDirectory(uploadsKlasoru);
                }

                var dosyaAdi = $"{Guid.NewGuid()}{uzanti}";
                var tamYol = Path.Combine(uploadsKlasoru, dosyaAdi);

                await using var stream = new FileStream(tamYol, FileMode.Create);
                await fotograf.CopyToAsync(stream);
                ariza.FotografYolu = $"/uploads/{dosyaAdi}";
            }

            await _db.SaveChangesAsync();
            TempData["Mesaj"] = "Arıza bildiriminiz güncellendi.";
        }
        catch
        {
            TempData["Hata"] = "Arıza güncellenirken bir hata oluştu.";
        }

        return RedirectToAction("Panel");
    }

    [Authorize(Roles = "Vatandas")]
    [HttpPost]
    public async Task<IActionResult> ArizaSil(int id)
    {
        try
        {
            var vatandasId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var ariza = await _db.Arizalar.FirstOrDefaultAsync(x => x.Id == id && x.VatandasId == vatandasId);

            if (ariza == null)
            {
                TempData["Hata"] = "Silinecek arıza bulunamadı.";
                return RedirectToAction("Panel");
            }

            DosyayiSil(ariza.FotografYolu);
            _db.Arizalar.Remove(ariza);
            await _db.SaveChangesAsync();

            TempData["Mesaj"] = "Arıza bildiriminiz silindi.";
        }
        catch
        {
            TempData["Hata"] = "Arıza silinirken bir hata oluştu.";
        }

        return RedirectToAction("Panel");
    }

    [Authorize(Roles = "Vatandas")]
    [HttpGet]
    public IActionResult SifreDegistir() => View();

    [Authorize(Roles = "Vatandas")]
    [HttpPost]
    public async Task<IActionResult> SifreDegistir(string mevcutSifre, string yeniSifre, string yeniSifreTekrar)
    {
        var vatandasId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var vatandas = await _db.Vatandaslar.FindAsync(vatandasId);

        if (vatandas == null || vatandas.Sifre != mevcutSifre)
        {
            ViewBag.Hata = "Mevcut şifreniz hatalı.";
            return View();
        }

        if (yeniSifre != yeniSifreTekrar)
        {
            ViewBag.Hata = "Yeni şifreler birbiriyle uyuşmuyor.";
            return View();
        }

        vatandas.Sifre = yeniSifre;
        await _db.SaveChangesAsync();
        ViewBag.Mesaj = "Şifreniz başarıyla güncellendi.";
        return View();
    }

    [HttpGet]
    public IActionResult SifremiUnuttum()
    {
        GuvenlikKoduHazirla("VatandasUnuttumKod");
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> SifremiUnuttum(string eposta, string guvenlikKodu)
    {
        if (!GuvenlikKoduDogruMu("VatandasUnuttumKod", guvenlikKodu))
        {
            ViewBag.Hata = "Güvenlik kodu hatalı.";
            GuvenlikKoduHazirla("VatandasUnuttumKod");
            return View();
        }

        var temizEposta = EpostaTemizle(eposta);
        var vatandas = await _db.Vatandaslar.FirstOrDefaultAsync(x => x.Eposta == temizEposta && x.EpostaOnaylandi);
        if (vatandas != null && !EpostaEngelliMi(temizEposta))
        {
            vatandas.SifreSifirlamaToken = TokenUret();
            vatandas.SifreSifirlamaTokenTarihi = DateTime.Now;
            await _db.SaveChangesAsync();
            var url = Url.Action("SifreSifirla", "Vatandas", new { token = vatandas.SifreSifirlamaToken }, Request.Scheme)!;
            await _emailService.GonderAsync(temizEposta, "Şifre sıfırlama", "Şifrenizi sıfırlayın", "Yeni şifre belirlemek için aşağıdaki butona basın.", "Şifreyi Sıfırla", url);
        }

        ViewBag.Mesaj = "E-posta kayıtlıysa şifre sıfırlama bağlantısı gönderildi.";
        GuvenlikKoduHazirla("VatandasUnuttumKod");
        return View();
    }

    [HttpGet]
    public IActionResult SifreSifirla(string token) => View(model: token);

    [HttpPost]
    public async Task<IActionResult> SifreSifirla(string token, string yeniSifre, string yeniSifreTekrar)
    {
        var vatandas = await _db.Vatandaslar.FirstOrDefaultAsync(x => x.SifreSifirlamaToken == token);
        if (vatandas == null || vatandas.SifreSifirlamaTokenTarihi < DateTime.Now.AddHours(-2))
        {
            ViewBag.Hata = "Şifre sıfırlama bağlantısı geçersiz veya süresi dolmuş.";
            return View(model: token);
        }

        if (yeniSifre != yeniSifreTekrar)
        {
            ViewBag.Hata = "Şifreler birbiriyle uyuşmuyor.";
            return View(model: token);
        }

        vatandas.Sifre = yeniSifre;
        vatandas.SifreSifirlamaToken = null;
        vatandas.SifreSifirlamaTokenTarihi = null;
        await _db.SaveChangesAsync();

        TempData["VatandasMesaj"] = "Şifreniz güncellendi. Yeni şifrenizle giriş yapabilirsiniz.";
        return RedirectToAction("Login");
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    private bool EpostaEngelliMi(string eposta)
    {
        return _db.EngellenenEpostalar.Any(x => x.Eposta == eposta);
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
            // Fotoğraf silinemese bile kullanıcı güncellemesini durdurmayalım.
        }
    }

    private void GuvenlikKoduHazirla(string key)
    {
        var kod = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        TempData[key] = kod;
        ViewBag.GuvenlikKodu = kod;
    }

    private bool GuvenlikKoduDogruMu(string key, string girilenKod)
    {
        var beklenenKod = TempData[key]?.ToString();
        return !string.IsNullOrWhiteSpace(beklenenKod) &&
               string.Equals(beklenenKod, girilenKod?.Trim(), StringComparison.Ordinal);
    }
}
