using System.Security.Claims;
using ArizaSikayet.Data;
using ArizaSikayet.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArizaSikayet.Controllers.Api;

[ApiController]
[Route("api/ariza")]
public class ArizaApiController : ControllerBase
{
    private readonly UygulamaDbContext _db;
    private readonly IWebHostEnvironment _env;

    // IWebHostEnvironment, wwwroot klasörüne güvenli erişim için eklendi
    public ArizaApiController(UygulamaDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    // 1. Haritada Gösterilecek Aktif Arızaları Getir (Tüm kullanıcılar görebilir)
    [HttpGet("aktifler")]
    public IActionResult GetAktifArizalar()
    {
        try
        {
            var veriler = _db.Arizalar
                .Include(x => x.Vatandas)
                .Where(x => !x.CozulduMu)
                .Select(x => new
                {
                    x.Id,
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

            return Ok(new { success = true, data = veriler });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Arızalar getirilirken bir hata oluştu.", details = ex.Message });
        }
    }

    // 2. Arıza Bildir (Vatandaş - Flutter'dan Multipart Form olarak gelecek)
    [HttpPost("bildir")]
    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Vatandas")]
    public async Task<IActionResult> Bildir([FromForm] ArizaBildirRequest request)
    {
        try
        {
            if (request.Fotograf == null || request.Fotograf.Length == 0)
            {
                return BadRequest(new { success = false, message = "Lütfen bir arıza fotoğrafı yükleyin." });
            }

            // Dosya Boyutu ve Tipi Kontrolü
            if (request.Fotograf.Length > 10485760) // 10MB
            {
                return BadRequest(new { success = false, message = "Dosya boyutu 10MB'dan büyük olamaz." });
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var extension = Path.GetExtension(request.Fotograf.FileName).ToLowerInvariant();
            
            if (!allowedExtensions.Contains(extension))
            {
                return BadRequest(new { success = false, message = "Sadece JPG, JPEG veya PNG formatında resim yükleyebilirsiniz." });
            }

            // Fotoğraf Kaydetme
            string dosyaAdi = Guid.NewGuid().ToString() + extension;
            string klasorYolu = Path.Combine(_env.WebRootPath, "uploads");

            if (!Directory.Exists(klasorYolu))
                Directory.CreateDirectory(klasorYolu);

            string tamYol = Path.Combine(klasorYolu, dosyaAdi);

            using (var stream = new FileStream(tamYol, FileMode.Create))
            {
                await request.Fotograf.CopyToAsync(stream);
            }

            // Vatandaş ID'sini Token'dan alma
            var vatandasIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(vatandasIdStr, out int vatandasId))
            {
                return Unauthorized(new { success = false, message = "Kullanıcı kimliği doğrulanamadı." });
            }

            // DB Kayıt
            var kategori = ArizaKategorileri.Normalize(request.Kategori);
            if (!ArizaKategorileri.GecerliMi(kategori))
            {
                return BadRequest(new { success = false, message = "Geçerli bir kategori seçin." });
            }

            var yeniAriza = new Ariza
            {
                Baslik = request.Baslik,
                Aciklama = request.Aciklama,
                Enlem = request.Enlem,
                Boylam = request.Boylam,
                AdresTarifi = request.AdresTarifi,
                Kategori = kategori,
                FotografYolu = "/uploads/" + dosyaAdi,
                OlusturmaTarihi = DateTime.Now,
                GuncellenmeTarihi = DateTime.Now,
                CozulduMu = false,
                Durum = ArizaDurumu.Beklemede, // Default durum
                VatandasId = vatandasId
            };

            _db.Arizalar.Add(yeniAriza);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, message = "Arıza bildirimi başarıyla alındı.", arizaId = yeniAriza.Id });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Kayıt sırasında bir hata oluştu.", details = ex.Message });
        }
    }

    // 3. Durum Güncelle (Admin)
    [HttpPut("durum-guncelle/{id}")]
    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Admin")]
    public IActionResult DurumGuncelle(int id, [FromBody] DurumGuncelleRequest request)
    {
        try
        {
            var ariza = _db.Arizalar.Find(id);
            if (ariza == null) return NotFound(new { success = false, message = "Arıza bulunamadı." });

            ariza.Durum = request.YeniDurum;
            ariza.GuncellenmeTarihi = DateTime.Now;

            // Tamamlandı veya İptal durumlarında
            if (request.YeniDurum == ArizaDurumu.Tamamlandi || request.YeniDurum == ArizaDurumu.IptalEdildi)
            {
                ariza.CozulduMu = true;
                DosyayiSil(ariza.FotografYolu);
                ariza.FotografYolu = null;
            }

            if (request.YeniDurum == ArizaDurumu.IptalEdildi)
            {
                _db.Arizalar.Remove(ariza);
            }

            _db.SaveChanges();
            return Ok(new { success = true, message = "Durum başarıyla güncellendi." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Durum güncellenirken bir hata oluştu.", details = ex.Message });
        }
    }

    // 4. Arızayı Çözüldü İşaretle (Admin)
    [HttpPut("coz/{id}")]
    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Admin")]
    public async Task<IActionResult> Coz(int id)
    {
        try
        {
            var ariza = await _db.Arizalar.FindAsync(id);
            if (ariza == null) return NotFound(new { success = false, message = "Arıza bulunamadı." });

            DosyayiSil(ariza.FotografYolu);
            ariza.CozulduMu = true;
            ariza.FotografYolu = null;
            ariza.Durum = ArizaDurumu.Tamamlandi;
            ariza.GuncellenmeTarihi = DateTime.Now;

            await _db.SaveChangesAsync();
            return Ok(new { success = true, message = "Arıza çözüldü olarak işaretlendi." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "İşlem sırasında bir hata oluştu.", details = ex.Message });
        }
    }

    // 5. Admin Paneli İçin Toplu Veri (Tüm listeleri tek JSON'da dönme)
    [HttpGet("admin-panel-verileri")]
    [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Admin")]
    public IActionResult GetAdminPanelVerileri()
    {
        try
        {
            var data = new
            {
                Arizalar = _db.Arizalar
                    .Include(x => x.Vatandas)
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
                    .ToList(),
                KayitIstekleri = _db.PersonelKayitIstekleri
                                    .Where(x => x.Durum == PersonelKayitDurumu.Beklemede)
                                    .OrderByDescending(x => x.TalepTarihi).ToList(),
                Vatandaslar = _db.Vatandaslar.OrderByDescending(x => x.OlusturmaTarihi).ToList(),
                Personeller = _db.Personeller.OrderByDescending(x => x.OlusturmaTarihi).ToList(),
                EngellenenEpostalar = _db.EngellenenEpostalar.OrderByDescending(x => x.OlusturmaTarihi).ToList()
            };

            return Ok(new { success = true, data = data });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Admin panel verileri alınırken bir hata oluştu.", details = ex.Message });
        }
    }

    // Yardımcı Metot (Private kalmaya devam ediyor, try-catch zaten vardı, biraz daha iyileştirildi)
    private void DosyayiSil(string? fotografYolu)
    {
        try
        {
            if (!string.IsNullOrEmpty(fotografYolu))
            {
                // _env.WebRootPath wwwroot klasörünün tam yolunu verir
                var tamYol = Path.Combine(_env.WebRootPath, fotografYolu.TrimStart('/'));

                if (System.IO.File.Exists(tamYol))
                {
                    System.IO.File.Delete(tamYol);
                }
            }
        }
        catch (Exception ex)
        {
            // API'yi çökertmemek için hatayı yutuyoruz, ancak loglama mekanizması (Serilog, NLog vb.) varsa buraya yazılabilir.
            Console.WriteLine($"Dosya silme hatası: {ex.Message}");
        }
    }
}

// --- FLUTTER İÇİN GEREKLİ DTO MODELLERİ ---

public class ArizaBildirRequest
{
    public string Baslik { get; set; } = null!;
    public string Aciklama { get; set; } = null!;
    public double Enlem { get; set; }
    public double Boylam { get; set; }
    public string? AdresTarifi { get; set; }
    public string Kategori { get; set; } = null!;
    public IFormFile Fotograf { get; set; } = null!; 
}

public class DurumGuncelleRequest
{
    public ArizaDurumu YeniDurum { get; set; }
}
