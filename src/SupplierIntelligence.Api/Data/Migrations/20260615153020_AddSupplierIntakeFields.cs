using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplierIntelligence.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierIntakeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CertificationHints",
                table: "Suppliers",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistryNumber",
                table: "Suppliers",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatNumber",
                table: "Suppliers",
                type: "TEXT",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificationHints",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "RegistryNumber",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "VatNumber",
                table: "Suppliers");
        }
    }
}
