namespace LyraSAT.DTOs
{
    public class CreateCommentDto
    {
        public int QuestionId { get; set; }
        public int UserId { get; set; }
        public int? ParentCommentId { get; set; }
        public int? ParentId { get; set; } // Hỗ trợ frontend truyền parentId
        public string Content { get; set; } = string.Empty;
        public string? AttachmentUrl { get; set; }
        public string? AttachmentType { get; set; } // "image" | "audio"
        public int? AudioDuration { get; set; }
    }

    public class UpdateCommentDto
    {
        public int UserId { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public class ToggleReactionDto
    {
        public int UserId { get; set; }
        public string ReactionType { get; set; } = "heart";
    }

    public class PinCommentDto
    {
        public int UserId { get; set; }
    }

    public class CommentItemDto
    {
        public int Id { get; set; }
        public int QuestionId { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string UserRole { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsEdited { get; set; }
        public int? ParentCommentId { get; set; }
        public bool IsPinned { get; set; }
        public DateTime? PinnedAt { get; set; }
        public string? AttachmentUrl { get; set; }
        public string? AttachmentType { get; set; }
        public int? AudioDuration { get; set; }
        public bool IsDeleted { get; set; }
        public int LikesCount { get; set; }
        public bool IsLikedByMe { get; set; }
        public List<CommentItemDto> Replies { get; set; } = new List<CommentItemDto>();
    }
}
