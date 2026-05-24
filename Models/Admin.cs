public class Admin
{
    public int Id { get; set; }
    public string KullaniciAdi { get; set; }
    public string Sifre { get; set; } // Gerçek projede bu hash'lenmiş olmalı
}