namespace LyraSAT.DTOs
{
    public class LoginDto
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class RegisterDto
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }      // "Student" hoặc "Teacher"
        public string? ClassCode { get; set; } // Ví dụ: "SAT01"
    }

    public class UpdateProfileDto
    {
        public int UserId { get; set; }
        public string? Nickname { get; set; }
        public string? School { get; set; }
        public string? Dob { get; set; }
        public string? Avatar { get; set; }
        public string? ExamDate { get; set; }
    }
}
