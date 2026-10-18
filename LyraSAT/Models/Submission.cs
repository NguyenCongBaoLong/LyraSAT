using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LyraSAT.Models
{
    public class Submission
    {
        [Key]
        public int Id { get; set; }

        public int StudentId { get; set; }

        [ForeignKey("StudentId")]
        public virtual User? Student { get; set; }

        public int TestId { get; set; }

        [ForeignKey("TestId")]
        public virtual Test? Test { get; set; }

        public int CorrectCount { get; set; } // Số câu làm đúng (Ví dụ: 3)

        public int TotalQuestions { get; set; } // Tổng số câu của đề thi (Ví dụ: 5)

        public double Score { get; set; } // Phần trăm điểm số % (Ví dụ: 60.0)

        public int TimeSpent { get; set; } // Thời gian làm bài (tính theo giây)

        public DateTime SubmittedAt { get; set; } = DateTime.Now;

        [StringLength(50)]
        public string Status { get; set; } = "Completed"; // Trạng thái: Completed / InProgress

        public virtual ICollection<SubmissionDetail>? SubmissionDetails { get; set; }
    }
}