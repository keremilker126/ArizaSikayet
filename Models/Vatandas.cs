using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace ArizaSikayet.Models
{
    public class Vatandas
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

        public bool EpostaOnaylandi { get; set; }

        [StringLength(80)]
        public string? KayitOnayToken { get; set; }

        public bool KayitOnayTokenKullanildi { get; set; }

        public string? SifreSifirlamaToken { get; set; }
        public DateTime? SifreSifirlamaTokenTarihi { get; set; }

        public DateTime OlusturmaTarihi { get; set; } = DateTime.Now;

        [JsonIgnore]
        public List<Ariza> Arizalar { get; set; } = new();
    }
}
