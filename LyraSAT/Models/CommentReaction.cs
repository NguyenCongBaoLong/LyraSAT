using System;

namespace LyraSAT.Models
{
    public class CommentReaction
    {
        public int Id { get; set; }
        public int CommentId { get; set; }
        public int UserId { get; set; }
        public string ReactionType { get; set; } = "heart"; // "heart" | "like"
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual QuestionComment? Comment { get; set; }
        public virtual User? User { get; set; }
    }
}
