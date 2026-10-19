using System.ComponentModel.DataAnnotations;

namespace LyraSAT.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public string Role { get; set; } // 'Student' hoặc 'Teacher'
        public string FullName { get; set; }
        public string? ClassCode { get; set; }
        public string? Avatar { get; set; }
        public DateTime? Dob { get; set; }
        public string? Nickname { get; set; }
        public int? TargetScore { get; set; }
        public bool? TuitionStatus { get; set; }

        public int StreakCount { get; set; } = 1;
        public DateTime? LastLoginDate { get; set; }
        public DateTime? ExamDate { get; set; }
        public string? School { get; set; }

        public ICollection<Submission> Submissions { get; set; }
    }
}
