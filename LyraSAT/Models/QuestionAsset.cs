using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LyraSAT.Models
{
    public class QuestionAsset
    {
        [Key]
        public int Id { get; set; }

        public int QuestionId { get; set; }

        [ForeignKey(nameof(QuestionId))]
        public Question? Question { get; set; }

        public string AssetType { get; set; } = "Image";
        public string FileUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string MimeType { get; set; } = "application/octet-stream";
        public string? AltText { get; set; }
        public string Placement { get; set; } = "Question";
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
