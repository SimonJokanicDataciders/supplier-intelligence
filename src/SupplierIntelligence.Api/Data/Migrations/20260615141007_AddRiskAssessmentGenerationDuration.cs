using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplierIntelligence.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskAssessmentGenerationDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GenerationDurationMs",
                table: "RiskAssessments",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GenerationDurationMs",
                table: "RiskAssessments");
        }
    }
}
