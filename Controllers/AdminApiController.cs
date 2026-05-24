using System.Security.Claims;
using System.Security.Cryptography;
using ArizaSikayet.Data;
using ArizaSikayet.Models;
using ArizaSikayet.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArizaSikayet.Controllers.Api;

[ApiController]
[Route("api/admin")]
public class AdminApiController : ControllerBase
{
    private readonly UygulamaDbContext _db;
    private readonly ApiTokenService _apiTokenService;

    public AdminApiController(UygulamaDbContext db, ApiTokenService apiTokenService)
    {
        _db = db;
        _apiTokenService = apiTokenService;
    }

    [HttpGet("security-code")]
    public IActionResult GetSecurityCode()
    {
        var kod = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        HttpContext.Session.SetString("AdminLoginKod", kod);
        return Ok(new { success = true, code = kod });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest model)
    {
        var beklenenKod = HttpContext.Session.GetString("AdminLoginKod");

        if (string.IsNullOrEmpty(model.GuvenlikKodu) || model.GuvenlikKodu != beklenenKod)
        {
            return BadRequest(new { success = false, message = "Güvenlik kodu hatalı!" });
        }

        var admin = _db.Adminler.FirstOrDefault(x => x.KullaniciAdi == model.KullaniciAdi && x.Sifre == model.Sifre);
        if (admin == null)
        {
            return Unauthorized(new { success = false, message = "Kullanıcı adı veya şifre hatalı!" });
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new(ClaimTypes.Name, admin.KullaniciAdi),
            new(ClaimTypes.Role, "Admin")
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

        var token = _apiTokenService.TokenOlustur(claims);
        return Ok(new
        {
            success = true,
            message = "Giriş başarılı.",
            token = token.Token,
            expiresAt = token.ExpiresAt,
            data = new
            {
                admin.Id,
                admin.KullaniciAdi
            }
        });
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { success = true, message = "Başarıyla çıkış yapıldı." });
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Admin")]
    [HttpPost("change-password")]
    public IActionResult SifreDegistir([FromBody] SifreDegistirRequest model)
    {
        var adminAdi = User.Identity?.Name;
        var admin = _db.Adminler.FirstOrDefault(x => x.KullaniciAdi == adminAdi);

        if (admin == null || admin.Sifre != model.MevcutSifre)
        {
            return BadRequest(new { success = false, message = "Mevcut şifreniz hatalı!" });
        }

        if (model.YeniSifre != model.YeniSifreTekrar)
        {
            return BadRequest(new { success = false, message = "Yeni şifreler birbiriyle uyuşmuyor!" });
        }

        admin.Sifre = model.YeniSifre;
        _db.SaveChanges();

        return Ok(new { success = true, message = "Şifreniz başarıyla güncellendi." });
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Admin")]
    [HttpPost("block-email")]
    public IActionResult EpostaEngelle([FromBody] EngelRequest model)
    {
        var temizEposta = model.Eposta?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(temizEposta))
        {
            return BadRequest(new { success = false, message = "Geçersiz e-posta!" });
        }

        if (_db.EngellenenEpostalar.Any(x => x.Eposta == temizEposta))
        {
            return Conflict(new { success = false, message = "Bu e-posta zaten engellenmiş." });
        }

        _db.EngellenenEpostalar.Add(new EngellenenEposta
        {
            Eposta = temizEposta,
            Aciklama = model.Aciklama,
            OlusturmaTarihi = DateTime.Now
        });
        _db.SaveChanges();

        return Ok(new { success = true, message = "E-posta engellendi." });
    }

    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Admin")]
    [HttpDelete("unblock-email/{id}")]
    public IActionResult EpostaEngelKaldir(int id)
    {
        var engel = _db.EngellenenEpostalar.Find(id);
        if (engel == null)
        {
            return NotFound(new { success = false, message = "Kayıt bulunamadı." });
        }

        _db.EngellenenEpostalar.Remove(engel);
        _db.SaveChanges();

        return Ok(new { success = true, message = "Engel kaldırıldı." });
    }
}

public class LoginRequest
{
    public string KullaniciAdi { get; set; } = "";
    public string Sifre { get; set; } = "";
    public string GuvenlikKodu { get; set; } = "";
}

public class SifreDegistirRequest
{
    public string MevcutSifre { get; set; } = "";
    public string YeniSifre { get; set; } = "";
    public string YeniSifreTekrar { get; set; } = "";
}

public class EngelRequest
{
    public string Eposta { get; set; } = "";
    public string? Aciklama { get; set; }
}
