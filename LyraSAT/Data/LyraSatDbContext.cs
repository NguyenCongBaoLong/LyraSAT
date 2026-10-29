using LyraSAT.Models;
using Microsoft.EntityFrameworkCore;

namespace LyraSAT.Data
{
    public class LyraSatDbContext : DbContext
    {
        public LyraSatDbContext(DbContextOptions<LyraSatDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Test> Tests { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<Submission> Submissions { get; set; }
        public DbSet<SubmissionDetail> SubmissionDetails { get; set; }
        public DbSet<Vocabulary> Vocabularies { get; set; }
        public DbSet<Class> Classes { get; set; }
        public DbSet<StudentClass> StudentClasses { get; set; }
        public DbSet<QuestionComment> QuestionComments { get; set; }
        public DbSet<CommentReaction> CommentReactions { get; set; }
        public DbSet<Notification> Notifications { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Class>()
                .HasIndex(c => c.ClassCode);

            modelBuilder.Entity<Test>()
                .HasIndex(t => new { t.ClassCode, t.CreatedAt, t.Id });

            modelBuilder.Entity<Test>()
                .HasIndex(t => new { t.ClassCode, t.Deadline });

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email);

            modelBuilder.Entity<User>()
                .HasIndex(u => new { u.Role, u.ClassCode, u.FullName, u.Id });

            modelBuilder.Entity<User>()
                .HasIndex(u => new { u.Role, u.FullName, u.Id });

            modelBuilder.Entity<Submission>()
                .HasIndex(s => new { s.StudentId, s.SubmittedAt, s.Id });

            modelBuilder.Entity<Submission>()
                .HasIndex(s => new { s.TestId, s.SubmittedAt, s.Id });

            modelBuilder.Entity<Vocabulary>()
                .HasIndex(v => new { v.ClassCode, v.SeriesName, v.Id });

            // Indexes and Relations for Comments
            modelBuilder.Entity<QuestionComment>()
                .HasIndex(c => new { c.QuestionId, c.IsPinned, c.CreatedAt });

            modelBuilder.Entity<QuestionComment>()
                .HasIndex(c => c.ParentCommentId);

            modelBuilder.Entity<QuestionComment>()
                .HasOne(c => c.ParentComment)
                .WithMany(c => c.Replies)
                .HasForeignKey(c => c.ParentCommentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes and Relations for Reactions
            modelBuilder.Entity<CommentReaction>()
                .HasIndex(r => new { r.CommentId, r.UserId })
                .IsUnique();

            modelBuilder.Entity<CommentReaction>()
                .HasOne(r => r.Comment)
                .WithMany(c => c.Reactions)
                .HasForeignKey(r => r.CommentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes and Relations for Notifications
            modelBuilder.Entity<Notification>()
                .HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });

            modelBuilder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Notification>()
                .HasOne(n => n.Actor)
                .WithMany()
                .HasForeignKey(n => n.ActorId)
                .OnDelete(DeleteBehavior.SetNull);

            base.OnModelCreating(modelBuilder);
        }
    }
}
