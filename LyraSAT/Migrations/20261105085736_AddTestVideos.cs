using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LyraSAT.Migrations
{
    /// <inheritdoc />
    public partial class AddTestVideos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ghi chu: DB that (Supabase) khong co bang __EFMigrationsHistory, schema duoc tao tay.
            // Migration nay chi mo ta thay doi cua bang TestVideos de code va schema cung mot nguon.
            migrationBuilder.CreateTable(
                name: "TestVideos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TestId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Url = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestVideos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestVideos_Tests_TestId",
                        column: x => x.TestId,
                        principalTable: "Tests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestVideos_TestId_SortOrder_Id",
                table: "TestVideos",
                columns: new[] { "TestId", "SortOrder", "Id" });

            // Chuyen video don cu (Tests.VideoExplanationUrl) sang bang moi.
            migrationBuilder.Sql(@"
                INSERT INTO ""TestVideos"" (""TestId"", ""Title"", ""Url"", ""SortOrder"", ""CreatedAt"")
                SELECT t.""Id"", 'Video chữa bài', t.""VideoExplanationUrl"", 0, now() at time zone 'utc'
                FROM ""Tests"" t
                WHERE t.""VideoExplanationUrl"" IS NOT NULL
                  AND t.""VideoExplanationUrl"" <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM ""TestVideos"" v
                      WHERE v.""TestId"" = t.""Id"" AND v.""Url"" = t.""VideoExplanationUrl""
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestVideos");
        }
    }
}
