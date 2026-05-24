using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArizaSikayet.Migrations
{
    /// <inheritdoc />
    public partial class RemovePersonelUniqueCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Personeller_PersonelKodu",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "PersonelKodu",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "OlusanPersonelKodu",
                table: "PersonelKayitIstekleri");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PersonelKodu",
                table: "Personeller",
                type: "TEXT",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OlusanPersonelKodu",
                table: "PersonelKayitIstekleri",
                type: "TEXT",
                maxLength: 12,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Personeller_PersonelKodu",
                table: "Personeller",
                column: "PersonelKodu",
                unique: true);
        }
    }
}
