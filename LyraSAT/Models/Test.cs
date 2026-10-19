using System.ComponentModel.DataAnnotations;

namespace LyraSAT.Models
{
    public class Test
    {
        [Key]
        public int Id { get; set; }
        public string Title { get; set; }
        public string? ClassCode { get; set; }
        public int Duration { get; set; }
        public DateTime? Deadline { get; set; }
        public string? FileUrl { get; set; }
        public string? TestType { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int? MaxAttempts { get; set; }

        public ICollection<Question> Questions { get; set; }
        public ICollection<Submission> Submissions { get; set; }
    }
}
