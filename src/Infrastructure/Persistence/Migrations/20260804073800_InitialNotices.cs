using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HilmaAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IngestionCheckpoints",
                columns: table => new
                {
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastSeenPublicationDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionCheckpoints", x => x.Source);
                });

            migrationBuilder.CreateTable(
                name: "Notices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NoticeType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    BuyerName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    BuyerOrganizationType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CpvCodes = table.Column<List<string>>(type: "text[]", nullable: false),
                    EstimatedValue = table.Column<decimal>(type: "numeric", nullable: true),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    PublicationDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmissionDeadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Region = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notices_CpvCodes",
                table: "Notices",
                column: "CpvCodes")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_PublicationDate",
                table: "Notices",
                column: "PublicationDate");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_SubmissionDeadline",
                table: "Notices",
                column: "SubmissionDeadline");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IngestionCheckpoints");

            migrationBuilder.DropTable(
                name: "Notices");
        }
    }
}
