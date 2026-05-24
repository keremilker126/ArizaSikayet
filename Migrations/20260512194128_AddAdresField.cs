using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArizaSikayet.Migrations
{
    /// <inheritdoc />
    public partial class AddAdresField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdresTarifi",
                table: "Arizalar",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdresTarifi",
                table: "Arizalar");
        }
    }
}
