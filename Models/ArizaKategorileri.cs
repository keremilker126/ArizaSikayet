namespace ArizaSikayet.Models;

public static class ArizaKategorileri
{
    public const string Elektrik = "Elektrik";
    public const string Su = "Su";
    public const string Dogalgaz = "Doğalgaz";
    public const string Kanalizasyon = "Kanalizasyon";
    public const string HasarliYapi = "Hasarlı Yapı";
    public const string Yol = "Yol";
    public const string PeyzajPark = "Peyzaj / Park";
    public const string Temizlik = "Temizlik";
    public const string UlasimTrafik = "Ulaşım / Trafik";
    public const string Diger = "Diğer Arızalar";

    public static readonly string[] TumKategoriler =
    {
        Elektrik,
        Su,
        Dogalgaz,
        Kanalizasyon,
        HasarliYapi,
        Yol,
        PeyzajPark,
        Temizlik,
        UlasimTrafik,
        Diger
    };

    public static readonly IReadOnlyDictionary<string, string> Etiketler = new Dictionary<string, string>
    {
        [Elektrik] = "Elektrik",
        [Su] = "Su",
        [Dogalgaz] = "Doğalgaz",
        [Kanalizasyon] = "Kanalizasyon",
        [HasarliYapi] = "Hasarlı Yapı",
        [Yol] = "Yol",
        [PeyzajPark] = "Peyzaj / Park",
        [Temizlik] = "Temizlik",
        [UlasimTrafik] = "Ulaşım / Trafik",
        [Diger] = "Diğer Arızalar"
    };

    public static readonly IReadOnlyDictionary<string, string> IconluEtiketler = new Dictionary<string, string>
    {
        [Elektrik] = "⚡ Elektrik",
        [Su] = "💧 Su",
        [Dogalgaz] = "🔥 Doğalgaz",
        [Kanalizasyon] = "🕳️ Kanalizasyon",
        [HasarliYapi] = "🏚️ Hasarlı Yapı",
        [Yol] = "🛣️ Yol",
        [PeyzajPark] = "🌳 Peyzaj / Park",
        [Temizlik] = "🧹 Temizlik",
        [UlasimTrafik] = "🚦 Ulaşım / Trafik",
        [Diger] = "🔧 Diğer Arızalar"
    };

    public static string Normalize(string? kategori)
    {
        var temiz = (kategori ?? "").Trim();
        if (string.IsNullOrWhiteSpace(temiz))
        {
            return "";
        }

        return temiz switch
        {
            "Yol/Asfalt" => Yol,
            "Yol / Asfalt" => Yol,
            "Yol / Çukur / Asfalt" => Yol,
            "Aydınlatma" => Elektrik,
            "Sokak Lambası" => Elektrik,
            "Su Patlağı" => Su,
            "Su Patlağı / Arıza" => Su,
            "Kanalizasyon Taşması" => Kanalizasyon,
            "Diger Arizalar" => Diger,
            "Diger Arızalar" => Diger,
            "Diğer Arizalar" => Diger,
            _ when TumKategoriler.Contains(temiz) => temiz,
            _ => Diger
        };
    }

    public static bool GecerliMi(string? kategori)
    {
        return TumKategoriler.Contains(Normalize(kategori));
    }
}
