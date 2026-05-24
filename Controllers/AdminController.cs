using System.Security.Claims;
using System.Security.Cryptography;
using ArizaSikayet.Data;
using ArizaSikayet.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArizaSikayet.Controllers;

public class AdminController : Controller
{
    private readonly UygulamaDbContext _db;

    public AdminController(UygulamaDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult Login()
    {
        GuvenlikKoduHazirla("AdminLoginKod");
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string kadi, string sifre, string guvenlikKodu)
    {
        if (!GuvenlikKoduDogruMu("AdminLoginKod", guvenlikKodu))
        {
            ViewBag.Hata = "Güvenlik kodu hatalı!";
            GuvenlikKoduHazirla("AdminLoginKod");
            return View();
        }

        var admin = _db.Adminler.FirstOrDefault(x => x.KullaniciAdi == kadi && x.Sifre == sifre);
        if (admin != null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, admin.KullaniciAdi),
                new(ClaimTypes.Role, "Admin")
            };
            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

            return RedirectToAction("AdminPanel", "Ariza");
        }

        ViewBag.Hata = "Kullanıcı adı veya şifre hatalı!";
        GuvenlikKoduHazirla("AdminLoginKod");
        return View();
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Admin");
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public IActionResult SifreDegistir() => View();

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public IActionResult SifreDegistir(string mevcutSifre, string yeniSifre, string yeniSifreTekrar)
    {
        var adminAdi = User.Identity?.Name;
        var admin = _db.Adminler.FirstOrDefault(x => x.KullaniciAdi == adminAdi);

        if (admin == null || admin.Sifre != mevcutSifre)
        {
            ViewBag.Hata = "Mevcut şifreniz hatalı!";
            return View();
        }

        if (yeniSifre != yeniSifreTekrar)
        {
            ViewBag.Hata = "Yeni şifreler birbiriyle uyuşmuyor!";
            return View();
        }

        admin.Sifre = yeniSifre;
        _db.SaveChanges();

        ViewBag.Mesaj = "Şifreniz başarıyla güncellendi.";
        return View();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public IActionResult EpostaEngelle(string eposta, string? aciklama)
    {
        var temizEposta = EpostaTemizle(eposta);
        if (!string.IsNullOrWhiteSpace(temizEposta) && !_db.EngellenenEpostalar.Any(x => x.Eposta == temizEposta))
        {
            _db.EngellenenEpostalar.Add(new EngellenenEposta
            {
                Eposta = temizEposta,
                Aciklama = aciklama,
                OlusturmaTarihi = DateTime.Now
            });
            _db.SaveChanges();
        }

        return RedirectToAction("AdminPanel", "Ariza");
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public IActionResult EpostaEngelKaldir(int id)
    {
        var engel = _db.EngellenenEpostalar.Find(id);
        if (engel != null)
        {
            _db.EngellenenEpostalar.Remove(engel);
            _db.SaveChanges();
        }

        return RedirectToAction("AdminPanel", "Ariza");
    }

    private static string EpostaTemizle(string? eposta)
    {
        return (eposta ?? "").Trim().ToLowerInvariant();
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
