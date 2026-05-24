using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ArizaSikayet.Models;
using Microsoft.AspNetCore.Authorization;
using ArizaSikayet.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace ArizaSikayet.Controllers;

public class ArizaController : Controller
{
    private readonly UygulamaDbContext _db;

    public ArizaController(UygulamaDbContext db)
    {
        _db = db;
    }
    // KullanÄ±cÄ±nÄ±n haritayÄ± gÃ¶rdÃ¼ÄŸÃ¼ ve arÄ±zalarÄ± izlediÄŸi ekran
    public IActionResult Index()
    {
        ViewBag.Kategoriler = ArizaKategorileri.TumKategoriler;
        return View();
    }

    // Haritaya verileri JSON olarak gÃ¶nderen API benzeri metod
    [HttpGet]
    public JsonResult GetArizalar()
    {
        // Sadece Ã§Ã¶zÃ¼lmemiÅŸ (aktif) arÄ±zalarÄ± haritaya gÃ¶nderiyoruz
        var veriler = _db.Arizalar
        .Include(x => x.Vatandas)
        .Where(x => !x.CozulduMu)
        .Select(x => new
        {
            x.Enlem,
            x.Boylam,
            x.Baslik,
            x.Aciklama,
            x.Kategori,
            Durum = x.Durum.ToString(),
            x.FotografYolu,
            x.AdresTarifi,
            x.OlusturmaTarihi,
            BildirenAd = x.Vatandas != null ? x.Vatandas.KullaniciAdi : "Bilinmiyor",
            BildirenEposta = x.Vatandas != null ? x.Vatandas.Eposta : ""
        }).ToList();

        return Json(veriler);
    }
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public IActionResult DurumGuncelle(int id, ArizaDurumu yeniDurum)
    {
        try
        {
            var ariza = _db.Arizalar.Find(id);
            if (ariza == null) return NotFound();

            ariza.Durum = yeniDurum;
            ariza.GuncellenmeTarihi = DateTime.Now;

            if (yeniDurum == ArizaDurumu.Tamamlandi || yeniDurum == ArizaDurumu.IptalEdildi)
            {
                ariza.CozulduMu = true;
                DosyayiSil(ariza.FotografYolu);
                ariza.FotografYolu = null;
            }

            if (yeniDurum == ArizaDurumu.IptalEdildi)
            {
                _db.Arizalar.Remove(ariza);
            }

            _db.SaveChanges();
            TempData["Mesaj"] = "Arıza durumu güncellendi.";
        }
        catch
        {
            TempData["Hata"] = "Durum güncellenirken bir hata oluştu.";
        }

        return RedirectToAction("AdminPanel");
    }

    // Admin Paneli: TÃ¼m arÄ±zalarÄ±n listesi
    [Authorize(Roles = "Admin")] // Sadece admin girebilir
    public IActionResult AdminPanel()
    {
        var liste = _db.Arizalar
            .Include(x => x.Vatandas)
            .OrderByDescending(x => x.OlusturmaTarihi)
            .ToList();
        ViewBag.KayitIstekleri = _db.PersonelKayitIstekleri
            .Where(x => x.Durum == PersonelKayitDurumu.Beklemede)
            .OrderByDescending(x => x.TalepTarihi)
            .ToList();
        ViewBag.Vatandaslar = _db.Vatandaslar.OrderByDescending(x => x.OlusturmaTarihi).ToList();
        ViewBag.Personeller = _db.Personeller.OrderByDescending(x => x.OlusturmaTarihi).ToList();
        ViewBag.EngellenenEpostalar = _db.EngellenenEpostalar.OrderByDescending(x => x.OlusturmaTarihi).ToList();
        return View(liste);
    }


    // ArÄ±zayÄ± Ã§Ã¶zÃ¼ldÃ¼ olarak iÅŸaretleme
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public IActionResult Coz(int id)
    {
        var ariza = _db.Arizalar.Find(id);
        if (ariza != null)
        {
            // 1. Fiziksel DosyayÄ± Sil
            DosyayiSil(ariza.FotografYolu);

            // 2. VeritabanÄ± GÃ¼ncelleme
            ariza.CozulduMu = true;
            ariza.FotografYolu = null; // Opsiyonel: DB'deki yolu da temizle ki artÄ±k dosya olmadÄ±ÄŸÄ±nÄ± bilelim

            _db.SaveChanges();
        }
        return RedirectToAction("AdminPanel");
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<JsonResult> CozAjax(int id)
    {
        var ariza = _db.Arizalar.Find(id);
        if (ariza != null)
        {
            // 1. Fiziksel DosyayÄ± Sil
            DosyayiSil(ariza.FotografYolu);

            // 2. VeritabanÄ± GÃ¼ncelleme
            ariza.CozulduMu = true;
            ariza.FotografYolu = null; // Yolu temizle

            await _db.SaveChangesAsync();
            return Json(new { success = true });
        }
        return Json(new { success = false });
    }

    // TekrarÄ± Ã¶nlemek iÃ§in yardÄ±mcÄ± metod
    private void DosyayiSil(string fotografYolu)
    {
        if (!string.IsNullOrEmpty(fotografYolu))
        {
            // FotografYolu "/uploads/dosya.jpg" ÅŸeklinde olduÄŸu iÃ§in baÅŸÄ±na wwwroot ekleyip tam yolu buluyoruz
            var tamYol = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", fotografYolu.TrimStart('/'));

            if (System.IO.File.Exists(tamYol))
            {
                try
                {
                    System.IO.File.Delete(tamYol);
                }
                catch (Exception ex)
                {
                    // Loglama yapabilirsin, dosya o an baÅŸka bir iÅŸlemce kullanÄ±lÄ±yor olabilir
                }
            }
        }
    }
    // GET: ArÄ±za Bildirme SayfasÄ±
    [HttpGet]
    [Authorize(Roles = "Vatandas")]
    public IActionResult Bildir()
    {
        ViewBag.Kategoriler = ArizaKategorileri.TumKategoriler;
        GuvenlikKoduHazirla("ArizaBildirKod");
        return View();
    }

    // POST: ArÄ±za Kaydetme
    [HttpPost]
    [Authorize(Roles = "Vatandas")]
    [ValidateAntiForgeryToken] // CSRF saldÄ±rÄ±larÄ±na karÅŸÄ± gÃ¼venlik saÄŸlar
    public async Task<IActionResult> Bildir(Ariza model, IFormFile fotograf, string guvenlikKodu)
    {
        if (!GuvenlikKoduDogruMu("ArizaBildirKod", guvenlikKodu))
        {
            ModelState.AddModelError("guvenlikKodu", "Güvenlik kodu hatalı.");
        }

        model.Kategori = ArizaKategorileri.Normalize(model.Kategori);
        if (!ArizaKategorileri.GecerliMi(model.Kategori))
        {
            ModelState.AddModelError(nameof(model.Kategori), "Geçerli bir kategori seçin.");
        }

        // 1. Sunucu TarafÄ± DoÄŸrulamalarÄ±
        if (fotograf != null)
        {
            // Dosya Boyutu KontrolÃ¼ (10MB = 10 * 1024 * 1024 byte)
            if (fotograf.Length > 10485760)
            {
                ModelState.AddModelError("fotograf", "Dosya boyutu 10MB'dan bÃ¼yÃ¼k olamaz.");
            }

            // Dosya Tipi KontrolÃ¼
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var extension = Path.GetExtension(fotograf.FileName).ToLower();
            if (!allowedExtensions.Contains(extension))
            {
                ModelState.AddModelError("fotograf", "Sadece JPG, JPEG veya PNG formatÄ±nda resim yÃ¼kleyebilirsiniz.");
            }
        }
        else
        {
            ModelState.AddModelError("fotograf", "Lütfen bir arıza fotoğrafı yükleyin.");
        }

        // Model doğrulamadan geçmediyse formu hatalarla birlikte geri gönder
        if (!ModelState.IsValid)
        {
            ViewBag.Kategoriler = ArizaKategorileri.TumKategoriler;
            GuvenlikKoduHazirla("ArizaBildirKod");
            return View(model);
        }

        try
        {
            // 2. FotoÄŸrafÄ± Kaydetme Ä°ÅŸlemi
            if (fotograf != null && fotograf.Length > 0)
            {
                string dosyaAdi = Guid.NewGuid().ToString() + Path.GetExtension(fotograf.FileName);

                // Path.Combine kullanÄ±mÄ± daha gÃ¼venlidir
                string klasorYolu = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");

                // KlasÃ¶r yoksa oluÅŸtur
                if (!Directory.Exists(klasorYolu))
                    Directory.CreateDirectory(klasorYolu);

                string tamYol = Path.Combine(klasorYolu, dosyaAdi);

                using (var stream = new FileStream(tamYol, FileMode.Create))
                {
                    await fotograf.CopyToAsync(stream);
                }

                model.FotografYolu = "/uploads/" + dosyaAdi;
            }

            // 3. VeritabanÄ±na KayÄ±t
            model.OlusturmaTarihi = DateTime.Now;
            model.CozulduMu = false;
            model.GuncellenmeTarihi = DateTime.Now;
            model.VatandasId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            _db.Arizalar.Add(model);
            await _db.SaveChangesAsync();

            // BaÅŸarÄ±lÄ± iÅŸlem sonrasÄ± bilgilendirme (Opsiyonel)
            TempData["Mesaj"] = "Arıza bildirimi başarıyla alındı.";

            return RedirectToAction("Index", "Home"); // Kendi projenize gÃ¶re controller adÄ±nÄ± gÃ¼ncelleyin
        }
        catch (Exception ex)
        {
            // Hata durumunda loglama yapÄ±labilir
            ModelState.AddModelError("", "Kayıt sırasında bir hata oluştu: " + ex.Message);
            ViewBag.Kategoriler = ArizaKategorileri.TumKategoriler;
            GuvenlikKoduHazirla("ArizaBildirKod");
            return View(model);
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


