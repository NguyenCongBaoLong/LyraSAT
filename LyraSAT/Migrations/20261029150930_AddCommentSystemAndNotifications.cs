using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LyraSAT.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentSystemAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Submissions_StudentId",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_Submissions_TestId",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_QuestionComments_QuestionId",
                table: "QuestionComments");

            migrationBuilder.AddColumn<string>(
                name: "AttachmentType",
                table: "QuestionComments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentUrl",
                table: "QuestionComments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudioDuration",
                table: "QuestionComments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "QuestionComments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsEdited",
                table: "QuestionComments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "QuestionComments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PinnedAt",
                table: "QuestionComments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PinnedByUserId",
                table: "QuestionComments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "QuestionComments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommentReactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommentId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ReactionType = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentReactions_QuestionComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "QuestionComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommentReactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ActorId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    QuestionId = table.Column<int>(type: "integer", nullable: true),
                    TestId = table.Column<int>(type: "integer", nullable: true),
                    CommentId = table.Column<int>(type: "integer", nullable: true),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionComments_ParentCommentId",
                table: "QuestionComments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionComments_QuestionId_IsPinned_CreatedAt",
                table: "QuestionComments",
                columns: new[] { "QuestionId", "IsPinned", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommentReactions_CommentId_UserId",
                table: "CommentReactions",
                columns: new[] { "CommentId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommentReactions_UserId",
                table: "CommentReactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ActorId",
                table: "Notifications",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_QuestionComments_QuestionComments_ParentCommentId",
                table: "QuestionComments",
                column: "ParentCommentId",
                principalTable: "QuestionComments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuestionComments_QuestionComments_ParentCommentId",
                table: "QuestionComments");

            migrationBuilder.DropTable(
                name: "CommentReactions");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_QuestionComments_ParentCommentId",
                table: "QuestionComments");

            migrationBuilder.DropIndex(
                name: "IX_QuestionComments_QuestionId_IsPinned_CreatedAt",
                table: "QuestionComments");

            migrationBuilder.DropColumn(
                name: "AttachmentType",
                table: "QuestionComments");

            migrationBuilder.DropColumn(
                name: "AttachmentUrl",
                table: "QuestionComments");

            migrationBuilder.DropColumn(
                name: "AudioDuration",
                table: "QuestionComments");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "QuestionComments");

            migrationBuilder.DropColumn(
                name: "IsEdited",
                table: "QuestionComments");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "QuestionComments");

            migrationBuilder.DropColumn(
                name: "PinnedAt",
                table: "QuestionComments");

            migrationBuilder.DropColumn(
                name: "PinnedByUserId",
                table: "QuestionComments");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "QuestionComments");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_StudentId",
                table: "Submissions",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_TestId",
                table: "Submissions",
                column: "TestId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionComments_QuestionId",
                table: "QuestionComments",
                column: "QuestionId");
        }
    }
}
