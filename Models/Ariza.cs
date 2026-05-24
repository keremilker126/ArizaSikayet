using System.ComponentModel.DataAnnotations;

namespace ArizaSikayet.Models
{
    public class Ariza
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Başlık zorunludur.")]
        [StringLength(30)]
        public string Baslik { get; set; } = "";

        [Required(ErrorMessage = "Açıklama zorunludur.")]
        [StringLength(100)]
        public string Aciklama { get; set; } = "";

        public string Kategori { get; set; } = "";
        public string? FotografYolu { get; set; } = "";

        [Required(ErrorMessage = "Adres tarifi zorunludur.")]
        public string? AdresTarifi { get; set; }

        [Required(ErrorMessage = "Enlem zorunludur.")]
        public double Enlem { get; set; }

        [Required(ErrorMessage = "Boylam zorunludur.")]
        public double Boylam { get; set; }

        public DateTime OlusturmaTarihi { get; set; } = DateTime.Now;

        // --- DURUM YÖNETİMİ ---

        // Detaylı aşama takibi için
        public ArizaDurumu Durum { get; set; } = ArizaDurumu.Beklemede;

        // "İş bitti mi?" kontrolü için senin istediğin bool alan
        public bool CozulduMu { get; set; } = false;

        public DateTime GuncellenmeTarihi { get; set; } = DateTime.Now;

        public int? VatandasId { get; set; }
        public Vatandas? Vatandas { get; set; }
    }

    public enum ArizaDurumu
    {
        Beklemede = 0,      // Yeni ihbar
        Inceleniyor = 1,    // Ekip yolda/inceliyor
        Onariliyor = 2,     // Çalışma devam ediyor
        Tamamlandi = 3,     // Çözüldü
        IptalEdildi = 4     // Geçersiz
    }
}
