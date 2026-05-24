using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options; // Bunu ekle
using ArizaSikayet.Models; // EmailSettings nerede kayıtlıysa o namespace

namespace ArizaSikayet.Services
{
    public class EmailService
    {
        private readonly EmailSettings _ayarlar;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IOptions<EmailSettings> ayarlar, ILogger<EmailService> logger)
        {
            _ayarlar = ayarlar.Value;
            _logger = logger;
        }

        public async Task GonderAsync(string alici, string konu, string baslik, string mesaj, string butonMetni, string butonUrl)
        {
            try 
            {
                // Ayar kontrolü
                if (string.IsNullOrEmpty(_ayarlar.Password))
                {
                    _logger.LogError("Email hatası: Şifre (App Password) boş!");
                    return;
                }

                using var client = new SmtpClient(_ayarlar.SmtpServer, _ayarlar.Port)
                {
                    EnableSsl = _ayarlar.EnableSsl,
                    Credentials = new NetworkCredential(_ayarlar.Username, _ayarlar.Password)
                };

                using var mail = new MailMessage
                {
                    From = new MailAddress(_ayarlar.SenderEmail, _ayarlar.SenderName, Encoding.UTF8),
                    Subject = konu,
                    SubjectEncoding = Encoding.UTF8,
                    BodyEncoding = Encoding.UTF8,
                    IsBodyHtml = true,
                    Body = $@"
                        <div style=""font-family:Arial,sans-serif;max-width:620px;margin:auto;padding:24px;border:1px solid #e5e7eb;border-radius:12px"">
                            <h2 style=""margin:0 0 12px;color:#111827"">{WebUtility.HtmlEncode(baslik)}</h2>
                            <p style=""color:#4b5563;line-height:1.6"">{WebUtility.HtmlEncode(mesaj)}</p>
                            <p style=""margin:28px 0"">
                                <a href=""{butonUrl}"" style=""background:#111827;color:#ffffff;text-decoration:none;padding:12px 18px;border-radius:8px;font-weight:bold"">{WebUtility.HtmlEncode(butonMetni)}</a>
                            </p>
                            <p style=""font-size:12px;color:#6b7280"">Bağlantı:<br>{WebUtility.HtmlEncode(butonUrl)}</p>
                        </div>"
                };

                mail.To.Add(alici);
                await client.SendMailAsync(mail);
                _logger.LogInformation("Mail başarıyla gönderildi: {alici}", alici);
            }
            catch (Exception ex)
            {
                // Hata olursa konsolda (Terminalde) kırmızı log olarak görünür
                _logger.LogError(ex, "Mail gönderilirken hata oluştu!");
                throw; 
            }
        }
    }
}