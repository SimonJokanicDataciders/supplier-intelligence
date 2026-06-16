using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplierIntelligence.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificationVerificationNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VerificationNotes",
                table: "Certifications",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VerificationNotes",
                table: "Certifications");
        }
    }
}
