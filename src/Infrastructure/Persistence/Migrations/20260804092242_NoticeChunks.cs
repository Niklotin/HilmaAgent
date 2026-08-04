using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HilmaAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NoticeChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NoticeChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Section = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LotId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    VectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EmbeddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeChunks_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeChunks_EmbeddingModel",
                table: "NoticeChunks",
                column: "EmbeddingModel");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeChunks_NoticeId",
                table: "NoticeChunks",
                column: "NoticeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoticeChunks");
        }
    }
}
