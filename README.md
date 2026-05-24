# Arıza Şikayet Sistemi

Vatandaşların çevrede gördükleri arıza ve şikayetleri fotoğraf, adres tarifi ve harita konumu ile belediye ekiplerine iletebildiği; admin ve personel kullanıcılarının bu bildirimleri yönetebildiği ASP.NET Core tabanlı web uygulaması.

## Genel Bakış

Bu proje, arıza bildirim sürecini tek bir panelde toplamayı hedefler. Vatandaşlar sisteme kayıt olur, e-posta doğrulamasından sonra arıza bildirimi oluşturur ve kendi bildirimlerini takip eder. Personel kullanıcıları görevli oldukları kategoriye ait arızaları görür ve durumlarını günceller. Admin ise tüm sistemi, personel kayıt isteklerini, vatandaşları, personelleri ve engellenen e-posta listesini yönetir.

## Özellikler

- Vatandaş kayıt, giriş, e-posta onayı ve şifre sıfırlama
- Personel kayıt isteği, admin onayı ve görev kategorisine göre panel erişimi
- Admin girişi, şifre değiştirme ve merkezi yönetim paneli
- Harita üzerinden arıza konumu seçme ve aktif arızaları görüntüleme
- Fotoğraf zorunlu arıza bildirimi
- JPG, JPEG ve PNG dosya doğrulaması
- 10 MB fotoğraf boyutu sınırı
- Arıza durum takibi: `Beklemede`, `Inceleniyor`, `Onariliyor`, `Tamamlandi`, `IptalEdildi`
- Çözülen veya iptal edilen arızalarda ilişkili fotoğraf dosyasını temizleme
- E-posta engelleme ve engeli kaldırma
- Mobil uygulamalar için JSON ve multipart/form-data destekli API uçları
- Cookie tabanlı web oturumu ve API token tabanlı mobil oturum desteği

## Kullanıcı Rolleri

| Rol | Yetkiler |
| --- | --- |
| Vatandaş | Kayıt olur, e-posta onayı yapar, arıza bildirir, kendi bildirimlerini günceller veya siler. |
| Personel | Admin onayından sonra giriş yapar, kendi görev kategorisindeki arızaları listeler ve durumlarını günceller. |
| Admin | Tüm arızaları, personel kayıt isteklerini, kullanıcıları ve engellenen e-postaları yönetir. |

## Arıza Kategorileri

Sistemde kullanılan arıza kategorileri şunlardır:

- Elektrik
- Su
- Doğalgaz
- Kanalizasyon
- Hasarlı Yapı
- Yol
- Peyzaj / Park
- Temizlik
- Ulaşım / Trafik
- Diğer Arızalar

## Teknolojiler

| Katman | Teknoloji |
| --- | --- |
| Backend | ASP.NET Core MVC |
| Hedef Framework | .NET 10 |
| Veritabanı | SQLite |
| ORM | Entity Framework Core |
| Arayüz | Razor Views, Bootstrap, JavaScript |
| Harita | Leaflet tabanlı web arayüzü |
| Kimlik Doğrulama | Cookie Authentication, özel API token doğrulama |
| E-posta | SMTP üzerinden kayıt onayı ve şifre sıfırlama |

## Proje Yapısı

```text
ArizaSikayet/
├── Controllers/          MVC ve API controller dosyaları
├── Data/                 Entity Framework DbContext
├── Migrations/           Veritabanı migration dosyaları
├── Models/               Veritabanı modelleri ve yardımcı model sınıfları
├── Services/             E-posta ve API token servisleri
├── Views/                Razor sayfaları
├── wwwroot/              Statik dosyalar, CSS, JS, görseller ve yüklemeler
├── appsettings.json      Uygulama ayarları
├── Program.cs            Uygulama başlangıç ve servis yapılandırması
└── ArizaSikayet.csproj   Proje dosyası
```




## Yapılandırma

E-posta gönderimi için `appsettings.json` içindeki `EmailSettings` bölümü kullanılır.

```json
{
  "EmailSettings": {
    "SmtpServer": "smtp.gmail.com",
    "Port": 587,
    "EnableSsl": true,
    "SenderName": "Ariza Bildirim Sistemi",
    "SenderEmail": "ornek@eposta.com",
    "Username": "ornek@eposta.com",
    "Password": "uygulama-sifresi"
  }
}
```

Gerçek SMTP şifresini kaynak koda yazmak yerine geliştirme ortamında kullanıcı gizleri, ortam değişkenleri veya güvenli bir yapılandırma yöntemi kullanmanız önerilir.

## Temel Sayfalar

| Sayfa | Açıklama |
| --- | --- |
| `/` | Ana sayfa |
| `/Ariza` | Aktif arızaların harita görünümü |
| `/Ariza/Bildir` | Vatandaş arıza bildirim formu |
| `/Vatandas/Login` | Vatandaş girişi |
| `/Vatandas/Kayit` | Vatandaş kaydı |
| `/Vatandas/Panel` | Vatandaş bildirim paneli |
| `/Personel/Login` | Personel girişi |
| `/Personel/Kayit` | Personel kayıt isteği |
| `/Personel/Panel` | Personel görev paneli |
| `/Admin/Login` | Admin girişi |
| `/Ariza/AdminPanel` | Admin yönetim paneli |

## API Özeti

Mobil uygulamalar ve harici istemciler için başlıca API uçları:

| Metot | Uç | Açıklama |
| --- | --- | --- |
| GET | `/api/mobile/health` | Sistem ve veritabanı durumunu döner. |
| GET | `/api/mobile/kategoriler` | Arıza kategorilerini listeler. |
| POST | `/api/mobile/vatandas/login` | Vatandaş girişi yapar. |
| POST | `/api/mobile/personel/login` | Personel girişi yapar. |
| POST | `/api/mobile/admin/login` | Admin girişi yapar. |
| POST | `/api/mobile/vatandas/kayit` | Vatandaş kaydı oluşturur. |
| POST | `/api/mobile/personel/kayit` | Personel kayıt isteği oluşturur. |
| GET | `/api/mobile/arizalar` | Aktif arızaları listeler. |
| GET | `/api/mobile/arizalarim` | Vatandaşın kendi arızalarını listeler. |
| POST | `/api/mobile/arizalar` | Fotoğraflı arıza bildirimi oluşturur. |
| GET | `/api/mobile/personel/arizalar` | Personelin görev kategorisindeki arızaları listeler. |
| POST/PUT | `/api/mobile/personel/arizalar/{id}/durum` | Arıza durumunu günceller. |

Token gerektiren uçlarda `Authorization` başlığı kullanılmalıdır:

```text
Authorization: Bearer <token>
```

## Arıza Bildirimi Kuralları

- Başlık en fazla 30 karakter olabilir.
- Açıklama en fazla 100 karakter olabilir.
- Kategori geçerli sistem kategorilerinden biri olmalıdır.
- Adres tarifi zorunludur.
- Konum bilgisi Türkiye sınırları içinde olmalıdır.
- Fotoğraf zorunludur.
- Fotoğraf uzantısı `.jpg`, `.jpeg` veya `.png` olmalıdır.
- Fotoğraf boyutu 10 MB değerini geçmemelidir.

## Geliştirme Notları

- Veritabanı dosyası SQLite olarak kullanılır.
- Yüklenen fotoğraflar `wwwroot/uploads` klasöründe tutulur.
- Data Protection anahtarları `DataProtectionKeys` klasöründe saklanır.
- Uygulama varsayılan kültür olarak `tr-TR` kullanır.
- API cevaplarında JSON alan adları camelCase formatında üretilir.

## Lisans

Bu proje için lisans bilgisi belirtilmemiştir. Kullanım, dağıtım veya yayınlama öncesinde proje sahibi tarafından lisans tercihi eklenmelidir.
