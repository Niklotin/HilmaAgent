using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HilmaAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProcurementDocumentsUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProcurementDocumentsUrl",
                table: "Notices",
                type: "text",
                nullable: true);

            // Backfill from the payload already on disk. This is the case RawPayload was kept for:
            // adding a field to the mapping is a database operation, not a re-crawl of every notice
            // under a rate limiter. Legacy notices have no equivalent field and stay null.
            migrationBuilder.Sql("""
                UPDATE "Notices"
                SET "ProcurementDocumentsUrl" = "RawPayload" ->> 'procurementDocumentsUrl'
                WHERE "RawPayload" ->> 'procurementDocumentsUrl' IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcurementDocumentsUrl",
                table: "Notices");
        }
    }
}
