using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArizaSikayet.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonelKayitIstekleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersonelKayitIstekleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    KullaniciAdi = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Sifre = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    GorevKategori = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Durum = table.Column<int>(type: "INTEGER", nullable: false),
                    TalepTarihi = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SonucTarihi = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OlusanPersonelKodu = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonelKayitIstekleri", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Personeller_KullaniciAdi",
                table: "Personeller",
                column: "KullaniciAdi",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Personeller_PersonelKodu",
                table: "Personeller",
                column: "PersonelKodu",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonelKayitIstekleri");

            migrationBuilder.DropIndex(
                name: "IX_Personeller_KullaniciAdi",
                table: "Personeller");

            migrationBuilder.DropIndex(
                name: "IX_Personeller_PersonelKodu",
                table: "Personeller");
        }
    }
}
