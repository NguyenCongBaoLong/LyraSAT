using System.ComponentModel.DataAnnotations;

namespace LyraSAT.Models
{
    public class Vocabulary
    {
        [Key]
        public int Id { get; set; }

        [StringLength(50)]
        public string? ClassCode { get; set; } // Mã lớp (VD: SAT01)

        [StringLength(255)]
        public string SeriesName { get; set; } = string.Empty; // Tên bộ từ vựng (VD: Day 1 - Essential Words)

        public string Word { get; set; } = string.Empty; // Từ vựng

        public string Definition { get; set; } = string.Empty; // Định nghĩa / Nghĩa

        public string? AudioUrl { get; set; } // Link phát âm (nếu có)

        public string? Example { get; set; } // Câu ví dụ
    }
}