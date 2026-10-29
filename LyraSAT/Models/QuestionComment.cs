using System;
using System.Collections.Generic;

namespace LyraSAT.Models
{
    public class QuestionComment
    {
        public int Id { get; set; }
        public int QuestionId { get; set; }
        public int UserId { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public bool IsEdited { get; set; } = false;
        
        public int? ParentCommentId { get; set; }

        public bool IsPinned { get; set; } = false;
        public DateTime? PinnedAt { get; set; }
        public int? PinnedByUserId { get; set; }

        public string? AttachmentUrl { get; set; }
        public string? AttachmentType { get; set; } // "image" | "audio"
        public int? AudioDuration { get; set; } // duration in seconds for voice notes

        public bool IsDeleted { get; set; } = false;

        // Navigation properties
        public virtual Question? Question { get; set; }
        public virtual User? User { get; set; }
        public virtual QuestionComment? ParentComment { get; set; }
        public virtual ICollection<QuestionComment> Replies { get; set; } = new List<QuestionComment>();
        public virtual ICollection<CommentReaction> Reactions { get; set; } = new List<CommentReaction>();
    }
}
