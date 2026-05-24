using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArizaSikayet.Migrations
{
    /// <inheritdoc />
    public partial class extraduzeltme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "AdresTarifi",
                table: "Arizalar",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Durum",
                table: "Arizalar",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "GuncellenmeTarihi",
                table: "Arizalar",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Durum",
                table: "Arizalar");

            migrationBuilder.DropColumn(
                name: "GuncellenmeTarihi",
                table: "Arizalar");

            migrationBuilder.AlterColumn<string>(
                name: "AdresTarifi",
                table: "Arizalar",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");
        }
    }
}
