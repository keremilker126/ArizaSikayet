using System.ComponentModel.DataAnnotations;

namespace ArizaSikayet.Models
{
    public class EngellenenEposta
    {
        public int Id { get; set; }

        [Required]
        [EmailAddress]
        [StringLength(120)]
        public string Eposta { get; set; } = "";

        [StringLength(200)]
        public string? Aciklama { get; set; }

        public DateTime OlusturmaTarihi { get; set; } = DateTime.Now;
    }
}
