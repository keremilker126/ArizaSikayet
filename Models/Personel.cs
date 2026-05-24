using System.ComponentModel.DataAnnotations;

namespace ArizaSikayet.Models
{
    public class Personel
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

        public DateTime OlusturmaTarihi { get; set; } = DateTime.Now;

        public string? SifreSifirlamaToken { get; set; }
        public DateTime? SifreSifirlamaTokenTarihi { get; set; }
    }
}
