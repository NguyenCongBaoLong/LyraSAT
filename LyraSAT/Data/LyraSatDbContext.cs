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

    }
}
