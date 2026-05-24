using System.ComponentModel.DataAnnotations;

namespace ArizaSikayet.Models
{
    public class PersonelKayitIstegi
    {
        public int Id { get; set; }

        [Required]
        [StringLength(30)]
        public string KullaniciAdi { get; set; } = "";

        [Required]
        [EmailAddress]
        [StringLength(120)]
        public string Eposta { get; set; } = "";

        [Required]
        [StringLength(60)]
        public string Sifre { get; set; } = "";

        [Required]
        [StringLength(30)]
        public string GorevKategori { get; set; } = "";

        public PersonelKayitDurumu Durum { get; set; } = PersonelKayitDurumu.Beklemede;

        public DateTime TalepTarihi { get; set; } = DateTime.Now;

        public DateTime? SonucTarihi { get; set; }

        [StringLength(80)]
        public string? KayitTamamlamaToken { get; set; }

        public bool KayitTamamlamaTokenKullanildi { get; set; }
    }

    public enum PersonelKayitDurumu
    {
        Beklemede = 0,
        Onaylandi = 1,
        Reddedildi = 2
    }
}
