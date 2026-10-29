using System;

namespace LyraSAT.Models
{
    public class Notification
    {
        public int Id { get; set; }
        public int UserId { get; set; } // Recipient
        public int? ActorId { get; set; } // Sender/Trigger user
        public string Type { get; set; } = "comment_reply"; // "comment_reply" | "question_comment" | "system"
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int? QuestionId { get; set; }
        public int? TestId { get; set; }
        public int? CommentId { get; set; }
        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual User? User { get; set; }
        public virtual User? Actor { get; set; }
    }
}
