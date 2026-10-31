using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LyraSAT.Migrations
{
    public partial class EnableMultipleStudentClasses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "StudentClasses" ("UserId", "ClassId", "AssignedAt")
                SELECT u."Id", c."Id", CURRENT_TIMESTAMP
                FROM "Users" u
                INNER JOIN "Classes" c ON c."ClassCode" = u."ClassCode"
                WHERE u."Role" = 'Student'
                  AND u."ClassCode" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "StudentClasses" sc
                      WHERE sc."UserId" = u."Id" AND sc."ClassId" = c."Id"
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_StudentClasses_UserId_ClassId",
                table: "StudentClasses",
                columns: new[] { "UserId", "ClassId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StudentClasses_UserId_ClassId",
                table: "StudentClasses");
        }
    }
}
