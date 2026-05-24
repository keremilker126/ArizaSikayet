using ArizaSikayet.Data;
using ArizaSikayet.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ArizaSikayet.Controllers
{
    [ApiController]
    [Route("api/arizalar")]
    public class ApiController : ControllerBase
    {
        private readonly UygulamaDbContext _db;

        private static readonly string[] ArizaKonulari = ArizaKategorileri.TumKategoriler;

        public ApiController(UygulamaDbContext db)
        {
            _db = db;
        }

        [HttpGet("konular")]
        [HttpGet("kategoriler")]
        public IActionResult GetArizaKonulari()
        {
            return Ok(ArizaKonulari.Select(konu => new
            {
                value = konu,
                label = ArizaKategorileri.Etiketler[konu]
            }));
        }

        [HttpGet("arizalar")]
        public async Task<IActionResult> GetArizalar(
            [FromQuery] string? kategori,
            [FromQuery] string? durum,
            [FromQuery] bool includeTamamlanan = false)
        {
            var query = _db.Arizalar.AsNoTracking().AsQueryable();

            if (!includeTamamlanan)
            {
                query = query.Where(x => !x.CozulduMu);
            }

            if (!string.IsNullOrWhiteSpace(kategori))
            {
                var temizKategori = ArizaKategorileri.Normalize(kategori);
                query = query.Where(x => x.Kategori == temizKategori);
            }

            if (!string.IsNullOrWhiteSpace(durum) &&
                Enum.TryParse<ArizaDurumu>(durum, ignoreCase: true, out var durumEnum))
            {
                query = query.Where(x => x.Durum == durumEnum);
            }

            var liste = await query
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
                .ToListAsync();

            return Ok(liste);
        }

        [HttpGet("ariza/{id:int}")]
        public async Task<IActionResult> GetAriza(int id)
        {
            var ariza = await _db.Arizalar
                .AsNoTracking()
                .Include(x => x.Vatandas)
                .Where(x => x.Id == id)
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
                .FirstOrDefaultAsync();

            if (ariza == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "Arıza bulunamadı."
                });
            }

            return Ok(ariza);
        }

        [HttpPost("bildir")]
        [HttpPost("ekle")]
        [Authorize(AuthenticationSchemes = "ApiToken,Cookies", Roles = "Vatandas")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ArizaBildir([FromForm] ArizaBildirRequest request)
        {
            var hata = ArizaBildirimiDogrula(request);
            if (hata != null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = hata
                });
            }

            var fotografYolu = await FotografKaydet(request.Fotograf!);

            var ariza = new Ariza
            {
                Baslik = request.Baslik.Trim(),
                Aciklama = request.Aciklama.Trim(),
                Kategori = ArizaKategorileri.Normalize(request.Kategori),
                AdresTarifi = request.AdresTarifi.Trim(),
                Enlem = request.Enlem,
                Boylam = request.Boylam,
                FotografYolu = fotografYolu,
                OlusturmaTarihi = DateTime.Now,
                GuncellenmeTarihi = DateTime.Now,
                Durum = ArizaDurumu.Beklemede,
                CozulduMu = false,
                VatandasId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var vatandasId) ? vatandasId : null
            };

            _db.Arizalar.Add(ariza);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Arıza bildirimi başarıyla alındı.",
                data = new
                {
                    ariza.Id,
                    ariza.Baslik,
                    ariza.Kategori,
                    Durum = ariza.Durum.ToString(),
                    ariza.Enlem,
                    ariza.Boylam,
                    ariza.FotografYolu,
                    ariza.OlusturmaTarihi
                }
            });
        }

        private static string? ArizaBildirimiDogrula(ArizaBildirRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Baslik))
            {
                return "Başlık zorunludur.";
            }

            if (request.Baslik.Length > 30)
            {
                return "Başlık en fazla 30 karakter olabilir.";
            }

            if (string.IsNullOrWhiteSpace(request.Aciklama))
            {
                return "Açıklama zorunludur.";
            }

            if (request.Aciklama.Length > 100)
            {
                return "Açıklama en fazla 100 karakter olabilir.";
            }

            if (string.IsNullOrWhiteSpace(request.Kategori) || !ArizaKategorileri.GecerliMi(request.Kategori))
            {
                return "Geçerli bir arıza konusu seçin.";
            }

            if (string.IsNullOrWhiteSpace(request.AdresTarifi))
            {
                return "Adres tarifi zorunludur.";
            }

            if (request.Enlem < 35.8 || request.Enlem > 42.5 || request.Boylam < 25.5 || request.Boylam > 45.0)
            {
                return "Konum Türkiye sınırları içinde olmalıdır.";
            }

            if (request.Fotograf == null || request.Fotograf.Length == 0)
            {
                return "Arıza fotoğrafı zorunludur.";
            }

            if (request.Fotograf.Length > 10 * 1024 * 1024)
            {
                return "Fotoğraf 10MB'dan büyük olamaz.";
            }

            var uzanti = Path.GetExtension(request.Fotograf.FileName).ToLowerInvariant();
            var izinliUzantilar = new[] { ".jpg", ".jpeg", ".png" };
            if (!izinliUzantilar.Contains(uzanti))
            {
                return "Sadece JPG, JPEG veya PNG fotoğraf yüklenebilir.";
            }

            return null;
        }

        private static async Task<string> FotografKaydet(IFormFile fotograf)
        {
            var uploadsKlasoru = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            if (!Directory.Exists(uploadsKlasoru))
            {
                Directory.CreateDirectory(uploadsKlasoru);
            }

            var dosyaAdi = $"{Guid.NewGuid()}{Path.GetExtension(fotograf.FileName)}";
            var tamYol = Path.Combine(uploadsKlasoru, dosyaAdi);

            await using var stream = new FileStream(tamYol, FileMode.Create);
            await fotograf.CopyToAsync(stream);

            return $"/uploads/{dosyaAdi}";
        }
    }

    public class ArizaBildirRequest
    {
        public string Baslik { get; set; } = "";
        public string Aciklama { get; set; } = "";
        public string Kategori { get; set; } = "";
        public string AdresTarifi { get; set; } = "";
        public double Enlem { get; set; }
        public double Boylam { get; set; }
        public IFormFile? Fotograf { get; set; }
    }
}
