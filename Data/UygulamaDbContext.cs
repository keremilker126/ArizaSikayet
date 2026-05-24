using Microsoft.EntityFrameworkCore;
using ArizaSikayet.Models; // Models klasöründeki Ariza sınıfına erişmek için

namespace ArizaSikayet.Data
{
    public class UygulamaDbContext : DbContext
    {
        public UygulamaDbContext(DbContextOptions<UygulamaDbContext> options) : base(options) { }

        public DbSet<Ariza> Arizalar { get; set; }
        public DbSet<Admin> Adminler { get; set; }
        public DbSet<Personel> Personeller { get; set; }
        public DbSet<PersonelKayitIstegi> PersonelKayitIstekleri { get; set; }
        public DbSet<Vatandas> Vatandaslar { get; set; }
        public DbSet<EngellenenEposta> EngellenenEpostalar { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Veritabanı ilk oluşurken varsayılan ayarlar buraya yazılabilir
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Personel>()
                .HasIndex(x => x.KullaniciAdi)
                .IsUnique();

            modelBuilder.Entity<Personel>()
                .HasIndex(x => x.Eposta)
                .IsUnique();

            modelBuilder.Entity<Vatandas>()
                .HasIndex(x => x.Eposta)
                .IsUnique();

            modelBuilder.Entity<EngellenenEposta>()
                .HasIndex(x => x.Eposta)
                .IsUnique();
        }
    }
}
