using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LyraSAT.Migrations
{
    /// <inheritdoc />
    public partial class AddReadQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Classes_ClassCode",
                table: "Classes",
                column: "ClassCode");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_StudentId_SubmittedAt_Id",
                table: "Submissions",
                columns: new[] { "StudentId", "SubmittedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_TestId_SubmittedAt_Id",
                table: "Submissions",
                columns: new[] { "TestId", "SubmittedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Tests_ClassCode_CreatedAt_Id",
                table: "Tests",
                columns: new[] { "ClassCode", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Tests_ClassCode_Deadline",
                table: "Tests",
                columns: new[] { "ClassCode", "Deadline" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Role_ClassCode_FullName_Id",
                table: "Users",
                columns: new[] { "Role", "ClassCode", "FullName", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Role_FullName_Id",
                table: "Users",
                columns: new[] { "Role", "FullName", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Vocabularies_ClassCode_SeriesName_Id",
                table: "Vocabularies",
                columns: new[] { "ClassCode", "SeriesName", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Classes_ClassCode",
                table: "Classes");

            migrationBuilder.DropIndex(
                name: "IX_Submissions_StudentId_SubmittedAt_Id",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_Submissions_TestId_SubmittedAt_Id",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_Tests_ClassCode_CreatedAt_Id",
                table: "Tests");

            migrationBuilder.DropIndex(
                name: "IX_Tests_ClassCode_Deadline",
                table: "Tests");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Role_ClassCode_FullName_Id",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Role_FullName_Id",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Vocabularies_ClassCode_SeriesName_Id",
                table: "Vocabularies");
        }
    }
}
