using System.ComponentModel.DataAnnotations;

namespace LyraSAT.Models
{
    /// <summary>
    /// Một video chữa bài của đề. Một đề được gắn nhiều video
    /// (ví dụ: video tổng kết, video từng phần, video riêng từng câu).
    /// </summary>
    public class TestVideo
    {
        [Key]
        public int Id { get; set; }

        public int TestId { get; set; }
        public Test? Test { get; set; }

        // Tiêu đề ngắn để học sinh phân biệt các video (có thể để trống).
        public string? Title { get; set; }

        // Link YouTube / Google Drive hoặc đường dẫn file đã upload (/uploads/...).
        public string Url { get; set; } = string.Empty;

        // Thứ tự hiển thị trong danh sách video của đề.
        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
