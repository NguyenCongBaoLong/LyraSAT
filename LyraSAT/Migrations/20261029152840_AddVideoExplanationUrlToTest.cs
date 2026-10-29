using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LyraSAT.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoExplanationUrlToTest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VideoExplanationUrl",
                table: "Tests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VideoExplanationUrl",
                table: "Tests");
        }
    }
}
