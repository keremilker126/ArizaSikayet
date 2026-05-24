using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArizaSikayet.Migrations
{
    /// <inheritdoc />
    public partial class AddVatandasAuthAndEmailBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Eposta",
                table: "Personeller",
                type: "TEXT",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SifreSifirlamaToken",
                table: "Personeller",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SifreSifirlamaTokenTarihi",
                table: "Personeller",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Eposta",
                table: "PersonelKayitIstekleri",
                type: "TEXT",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "KayitTamamlamaToken",
                table: "PersonelKayitIstekleri",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "KayitTamamlamaTokenKullanildi",
                table: "PersonelKayitIstekleri",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "VatandasId",
                table: "Arizalar",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EngellenenEpostalar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Eposta = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Aciklama = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    OlusturmaTarihi = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngellenenEpostalar", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vatandaslar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    KullaniciAdi = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Eposta = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Sifre = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    EpostaOnaylandi = table.Column<bool>(type: "INTEGER", nullable: false),
                    KayitOnayToken = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    KayitOnayTokenKullanildi = table.Column<bool>(type: "INTEGER", nullable: false),
                    SifreSifirlamaToken = table.Column<string>(type: "TEXT", nullable: true),
                    SifreSifirlamaTokenTarihi = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OlusturmaTarihi = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vatandaslar", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Personeller_Eposta",
                table: "Personeller",
                column: "Eposta",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Arizalar_VatandasId",
                table: "Arizalar",
                column: "VatandasId");

            migrationBuilder.CreateIndex(
                name: "IX_EngellenenEpostalar_Eposta",
                table: "EngellenenEpostalar",
                column: "Eposta",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vatandaslar_Eposta",
                table: "Vatandaslar",
                column: "Eposta",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Arizalar_Vatandaslar_VatandasId",
                table: "Arizalar",
                column: "VatandasId",
                principalTable: "Vatandaslar",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Arizalar_Vatandaslar_VatandasId",
                table: "Arizalar");

            migrationBuilder.DropTable(
                name: "EngellenenEpostalar");

            migrationBuilder.DropTable(
                name: "Vatandaslar");

            migrationBuilder.DropIndex(
                name: "IX_Personeller_Eposta",
                table: "Personeller");

            migrationBuilder.DropIndex(
                name: "IX_Arizalar_VatandasId",
                table: "Arizalar");

            migrationBuilder.DropColumn(
                name: "Eposta",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "SifreSifirlamaToken",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "SifreSifirlamaTokenTarihi",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "Eposta",
                table: "PersonelKayitIstekleri");

            migrationBuilder.DropColumn(
                name: "KayitTamamlamaToken",
                table: "PersonelKayitIstekleri");

            migrationBuilder.DropColumn(
                name: "KayitTamamlamaTokenKullanildi",
                table: "PersonelKayitIstekleri");

            migrationBuilder.DropColumn(
                name: "VatandasId",
                table: "Arizalar");
        }
    }
}
