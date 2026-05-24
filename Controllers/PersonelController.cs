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

public class PersonelController : Controller
{
    private readonly UygulamaDbContext _db;
    private readonly EmailService _emailService;

    private static readonly string[] Kategoriler = ArizaKategorileri.TumKategoriler;

    public PersonelController(UygulamaDbContext db, EmailService emailService)
    {
        _db = db;
        _emailService = emailService;
    }

    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true && User.IsInRole("Personel"))
        {
            return RedirectToAction("Panel");
        }

        GuvenlikKoduHazirla("PersonelLoginKod");
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string eposta, string sifre, string guvenlikKodu)
    {
        if (!GuvenlikKoduDogruMu("PersonelLoginKod", guvenlikKodu))
        {
            ViewBag.Hata = "Güvenlik kodu hatalı.";
            GuvenlikKoduHazirla("PersonelLoginKod");
            return View();
        }

        var personel = _db.Personeller.FirstOrDefault(x => x.Eposta == eposta && x.Sifre == sifre);
        if (personel != null && EpostaEngelliMi(personel.Eposta))
        {
            ViewBag.Hata = "Bu e-posta adresi sistem tarafından engellenmiş.";
            GuvenlikKoduHazirla("PersonelLoginKod");
            return View();
        }

        if (personel == null)
        {
            var bekleyenIstek = _db.PersonelKayitIstekleri.Any(x =>
                x.Eposta == eposta &&
                x.Sifre == sifre &&
                x.Durum == PersonelKayitDurumu.Beklemede);

            if (bekleyenIstek)
            {
                ViewBag.Hata = "Kayıt isteğiniz henüz admin onayı bekliyor.";
                GuvenlikKoduHazirla("PersonelLoginKod");
                return View();
            }

            ViewBag.Hata = "Kullanıcı adı veya şifre hatalı.";
            GuvenlikKoduHazirla("PersonelLoginKod");
            return View();
        }

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

        return RedirectToAction("Panel");
    }

    [HttpGet]
    public IActionResult Kayit()
    {
        ViewBag.Kategoriler = Kategoriler;
        GuvenlikKoduHazirla("PersonelKayitKod");
        return View();
    }

    [HttpPost]
    public IActionResult Kayit(string kullaniciAdi, string eposta, string sifre, string sifreTekrar, string gorevKategori, string guvenlikKodu)
    {
        ViewBag.Kategoriler = Kategoriler;

        if (!GuvenlikKoduDogruMu("PersonelKayitKod", guvenlikKodu))
        {
            ViewBag.Hata = "Güvenlik kodu hatalı.";
            GuvenlikKoduHazirla("PersonelKayitKod");
            return View();
        }

        var temizEposta = EpostaTemizle(eposta);

        if (string.IsNullOrWhiteSpace(kullaniciAdi) || string.IsNullOrWhiteSpace(temizEposta) || string.IsNullOrWhiteSpace(sifre))
        {
            ViewBag.Hata = "Kullanıcı adı, e-posta ve şifre zorunludur.";
            GuvenlikKoduHazirla("PersonelKayitKod");
            return View();
        }

        if (EpostaEngelliMi(temizEposta))
        {
            ViewBag.Hata = "Bu e-posta adresiyle kayıt yapılamaz.";
            GuvenlikKoduHazirla("PersonelKayitKod");
            return View();
        }

        if (sifre != sifreTekrar)
        {
            ViewBag.Hata = "Şifreler birbiriyle uyuşmuyor.";
            GuvenlikKoduHazirla("PersonelKayitKod");
            return View();
        }

        gorevKategori = ArizaKategorileri.Normalize(gorevKategori);

        if (!ArizaKategorileri.GecerliMi(gorevKategori))
        {
            ViewBag.Hata = "Geçerli bir görev kategorisi seçin.";
            GuvenlikKoduHazirla("PersonelKayitKod");
            return View();
        }

        var temizKullaniciAdi = kullaniciAdi.Trim();

        if (_db.Personeller.Any(x => x.KullaniciAdi == temizKullaniciAdi))
        {
            ViewBag.Hata = "Bu kullanıcı adı zaten kullanılıyor.";
            GuvenlikKoduHazirla("PersonelKayitKod");
            return View();
        }

        if (_db.Personeller.Any(x => x.Eposta == temizEposta) || _db.Vatandaslar.Any(x => x.Eposta == temizEposta))
        {
            ViewBag.Hata = "Bu e-posta adresiyle kayıtlı bir hesap var.";
            GuvenlikKoduHazirla("PersonelKayitKod");
            return View();
        }

        if (_db.PersonelKayitIstekleri.Any(x =>
            (x.KullaniciAdi == temizKullaniciAdi || x.Eposta == temizEposta) &&
            x.Durum == PersonelKayitDurumu.Beklemede))
        {
            ViewBag.Hata = "Bu kullanıcı adı veya e-posta için bekleyen bir kayıt isteği var.";
            GuvenlikKoduHazirla("PersonelKayitKod");
            return View();
        }

        var istek = new PersonelKayitIstegi
        {
            KullaniciAdi = temizKullaniciAdi,
            Eposta = temizEposta,
            Sifre = sifre,
            GorevKategori = gorevKategori,
            TalepTarihi = DateTime.Now,
            Durum = PersonelKayitDurumu.Beklemede
        };

        _db.PersonelKayitIstekleri.Add(istek);
        _db.SaveChanges();

        ViewBag.Mesaj = "Kayıt isteğiniz admin onayına gönderildi. Onaydan sonra e-postanıza kayıt tamamlama bağlantısı gelecek.";
        GuvenlikKoduHazirla("PersonelKayitKod");
        return View();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> KayitIstegiOnayla(int id)
    {
        var istek = _db.PersonelKayitIstekleri.Find(id);
        if (istek == null || istek.Durum != PersonelKayitDurumu.Beklemede)
        {
            return RedirectToAction("AdminPanel", "Ariza");
        }

        if (_db.Personeller.Any(x => x.KullaniciAdi == istek.KullaniciAdi || x.Eposta == istek.Eposta) ||
            _db.Vatandaslar.Any(x => x.Eposta == istek.Eposta) ||
            EpostaEngelliMi(istek.Eposta))
        {
            istek.Durum = PersonelKayitDurumu.Reddedildi;
            istek.SonucTarihi = DateTime.Now;
            _db.SaveChanges();
            return RedirectToAction("AdminPanel", "Ariza");
        }

        istek.Durum = PersonelKayitDurumu.Onaylandi;
        istek.SonucTarihi = DateTime.Now;
        istek.KayitTamamlamaToken = TokenUret();
        _db.SaveChanges();

        var url = Url.Action("KayitTamamla", "Personel", new { token = istek.KayitTamamlamaToken }, Request.Scheme)!;
        await _emailService.GonderAsync(istek.Eposta, "Personel kaydınızı tamamlayın", "Personel kaydınız onaylandı", "Kaydınızı tamamlamak için aşağıdaki butona basın. Bu bağlantı süresizdir ancak yalnızca bir kez kullanılabilir.", "Kaydı Tamamla", url);

        return RedirectToAction("AdminPanel", "Ariza");
    }

    [HttpGet]
    public IActionResult KayitTamamla(string token)
    {
        var istek = _db.PersonelKayitIstekleri.FirstOrDefault(x => x.KayitTamamlamaToken == token);
        if (istek == null || istek.Durum != PersonelKayitDurumu.Onaylandi || istek.KayitTamamlamaTokenKullanildi)
        {
            TempData["PersonelMesaj"] = "Kayıt bağlantısı geçersiz veya daha önce kullanılmış.";
            return RedirectToAction("Login");
        }

        if (_db.Personeller.Any(x => x.KullaniciAdi == istek.KullaniciAdi || x.Eposta == istek.Eposta) ||
            _db.Vatandaslar.Any(x => x.Eposta == istek.Eposta) ||
            EpostaEngelliMi(istek.Eposta))
        {
            TempData["PersonelMesaj"] = "Bu e-posta adresiyle kayıt tamamlanamadı.";
            return RedirectToAction("Login");
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

        TempData["PersonelMesaj"] = "Kaydınız tamamlandı. Artık giriş yapabilirsiniz.";
        return RedirectToAction("Login");
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public IActionResult KayitIstegiReddet(int id)
    {
        var istek = _db.PersonelKayitIstekleri.Find(id);
        if (istek != null && istek.Durum == PersonelKayitDurumu.Beklemede)
        {
            istek.Durum = PersonelKayitDurumu.Reddedildi;
            istek.SonucTarihi = DateTime.Now;
            _db.SaveChanges();
        }

        return RedirectToAction("AdminPanel", "Ariza");
    }

    [Authorize(Roles = "Personel")]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Panel()
    {
        var kategori = ArizaKategorileri.Normalize(User.FindFirst("GorevKategori")?.Value);
        var arizalar = _db.Arizalar
            .Include(x => x.Vatandas)
            .Where(x => x.Kategori == kategori)
            .OrderByDescending(x => x.OlusturmaTarihi)
            .ToList();

        ViewBag.Kategori = kategori;
        return View(arizalar);
    }

    [Authorize(Roles = "Personel")]
    [HttpPost]
    public IActionResult DurumGuncelle(int id, ArizaDurumu yeniDurum)
    {
        var kategori = ArizaKategorileri.Normalize(User.FindFirst("GorevKategori")?.Value);
        var ariza = _db.Arizalar.FirstOrDefault(x => x.Id == id && x.Kategori == kategori);

        if (ariza == null)
        {
            return Forbid();
        }

        ariza.Durum = yeniDurum;
        ariza.GuncellenmeTarihi = DateTime.Now;

        if (yeniDurum == ArizaDurumu.IptalEdildi)
        {
            DosyayiSil(ariza.FotografYolu);
            _db.Arizalar.Remove(ariza);
            _db.SaveChanges();
            return RedirectToAction("Panel");
        }

        if (yeniDurum == ArizaDurumu.Tamamlandi)
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
        return RedirectToAction("Panel");
    }

    [Authorize(Roles = "Personel")]
    [HttpGet]
    public IActionResult SifreDegistir()
    {
        return View();
    }

    [Authorize(Roles = "Personel")]
    [HttpPost]
    public IActionResult SifreDegistir(string mevcutSifre, string yeniSifre, string yeniSifreTekrar)
    {
        var personelIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(personelIdText, out var personelId))
        {
            return RedirectToAction("Login");
        }

        var personel = _db.Personeller.Find(personelId);
        if (personel == null || personel.Sifre != mevcutSifre)
        {
            ViewBag.Hata = "Mevcut şifreniz hatalı.";
            return View();
        }

        if (yeniSifre != yeniSifreTekrar)
        {
            ViewBag.Hata = "Yeni şifreler birbiriyle uyuşmuyor.";
            return View();
        }

        personel.Sifre = yeniSifre;
        _db.SaveChanges();

        ViewBag.Mesaj = "Şifreniz başarıyla güncellendi.";
        return View();
    }

    [HttpGet]
    public IActionResult SifremiUnuttum()
    {
        GuvenlikKoduHazirla("PersonelUnuttumKod");
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> SifremiUnuttum(string eposta, string guvenlikKodu)
    {
        if (!GuvenlikKoduDogruMu("PersonelUnuttumKod", guvenlikKodu))
        {
            ViewBag.Hata = "Güvenlik kodu hatalı.";
            GuvenlikKoduHazirla("PersonelUnuttumKod");
            return View();
        }

        var temizEposta = EpostaTemizle(eposta);
        var personel = _db.Personeller.FirstOrDefault(x => x.Eposta == temizEposta);
        if (personel != null && !EpostaEngelliMi(temizEposta))
        {
            personel.SifreSifirlamaToken = TokenUret();
            personel.SifreSifirlamaTokenTarihi = DateTime.Now;
            _db.SaveChanges();

            var url = Url.Action("SifreSifirla", "Personel", new { token = personel.SifreSifirlamaToken }, Request.Scheme)!;
            await _emailService.GonderAsync(temizEposta, "Personel şifre sıfırlama", "Şifrenizi sıfırlayın", "Yeni şifre belirlemek için aşağıdaki butona basın.", "Şifreyi Sıfırla", url);
        }

        ViewBag.Mesaj = "E-posta kayıtlıysa şifre sıfırlama bağlantısı gönderildi.";
        GuvenlikKoduHazirla("PersonelUnuttumKod");
        return View();
    }

    [HttpGet]
    public IActionResult SifreSifirla(string token)
    {
        return View(model: token);
    }

    [HttpPost]
    public IActionResult SifreSifirla(string token, string yeniSifre, string yeniSifreTekrar)
    {
        var personel = _db.Personeller.FirstOrDefault(x => x.SifreSifirlamaToken == token);
        if (personel == null || personel.SifreSifirlamaTokenTarihi < DateTime.Now.AddHours(-2))
        {
            ViewBag.Hata = "Şifre sıfırlama bağlantısı geçersiz veya süresi dolmuş.";
            return View(model: token);
        }

        if (yeniSifre != yeniSifreTekrar)
        {
            ViewBag.Hata = "Yeni şifreler birbiriyle uyuşmuyor.";
            return View(model: token);
        }

        personel.Sifre = yeniSifre;
        personel.SifreSifirlamaToken = null;
        personel.SifreSifirlamaTokenTarihi = null;
        _db.SaveChanges();

        TempData["PersonelMesaj"] = "Şifreniz güncellendi. Yeni şifrenizle giriş yapabilirsiniz.";
        return RedirectToAction("Login");
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
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
        if (string.IsNullOrWhiteSpace(fotografYolu))
        {
            return;
        }

        var tamYol = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", fotografYolu.TrimStart('/'));

        if (!System.IO.File.Exists(tamYol))
        {
            return;
        }

        try
        {
            System.IO.File.Delete(tamYol);
        }
        catch
        {
            // Dosya o anda kullanımdaysa kayıt güncellemesini engellemeyelim.
        }
    }
}
