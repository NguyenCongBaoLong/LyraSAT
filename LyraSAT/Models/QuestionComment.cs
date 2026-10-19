namespace LyraSAT.Models
{
    public class QuestionComment
    {
        public int Id { get; set; }
        public int QuestionId { get; set; }
        public int UserId { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int? ParentCommentId { get; set; }

        // Navigation properties
        public virtual Question? Question { get; set; }
        public virtual User? User { get; set; }
    }
}
