using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HilmaAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EditedRecommendation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ReviewerNote = table.Column<string>(type: "text", nullable: true),
                    ReviewedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalDecisions_FitAssessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "FitAssessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_AssessmentId",
                table: "ApprovalDecisions",
                column: "AssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_DecidedAt",
                table: "ApprovalDecisions",
                column: "DecidedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalDecisions");
        }
    }
}
