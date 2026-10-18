using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LyraSAT.Models
{
    public class SubmissionDetail
    {
        [Key]
        public int Id { get; set; }

        public int SubmissionId { get; set; }

        [ForeignKey("SubmissionId")]
        public virtual Submission? Submission { get; set; }

        public int QuestionId { get; set; }

        [ForeignKey("QuestionId")]
        public virtual Question? Question { get; set; }

        [StringLength(10)]
        public string SelectedAnswer { get; set; } = string.Empty; // Đáp án học sinh chọn: "A", "B", "C", "D"

        public bool IsCorrect { get; set; } // Trạng thái câu trả lời: true (Đúng), false (Sai)
    }
}