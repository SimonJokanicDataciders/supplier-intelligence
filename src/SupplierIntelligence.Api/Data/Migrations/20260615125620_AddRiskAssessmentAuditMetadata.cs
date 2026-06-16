using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplierIntelligence.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskAssessmentAuditMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvidenceSnapshotJson",
                table: "RiskAssessments",
                type: "TEXT",
                maxLength: 12000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromptFocus",
                table: "RiskAssessments",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EvidenceSnapshotJson",
                table: "RiskAssessments");

            migrationBuilder.DropColumn(
                name: "PromptFocus",
                table: "RiskAssessments");
        }
    }
}
