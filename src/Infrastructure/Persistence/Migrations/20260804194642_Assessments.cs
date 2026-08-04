using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HilmaAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Assessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Technologies = table.Column<List<string>>(type: "text[]", nullable: false),
                    ReferenceProjects = table.Column<List<string>>(type: "text[]", nullable: false),
                    PreferredCpvCodes = table.Column<List<string>>(type: "text[]", nullable: false),
                    Regions = table.Column<List<string>>(type: "text[]", nullable: false),
                    MinContractValue = table.Column<decimal>(type: "numeric", nullable: true),
                    MaxContractValue = table.Column<decimal>(type: "numeric", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FitAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeterministicScore = table.Column<int>(type: "integer", nullable: false),
                    ScoreBreakdownJson = table.Column<string>(type: "jsonb", nullable: false),
                    ScoreRecommendation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ModelRecommendation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RecommendationDisagreement = table.Column<bool>(type: "boolean", nullable: false),
                    Reasoning = table.Column<string>(type: "text", nullable: false),
                    ModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Citations = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FitAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FitAssessments_CompanyProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "CompanyProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FitAssessments_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FitAssessments_CreatedAt",
                table: "FitAssessments",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FitAssessments_NoticeId",
                table: "FitAssessments",
                column: "NoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_FitAssessments_ProfileId",
                table: "FitAssessments",
                column: "ProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FitAssessments");

            migrationBuilder.DropTable(
                name: "CompanyProfiles");
        }
    }
}
