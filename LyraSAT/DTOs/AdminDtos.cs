namespace LyraSAT.DTOs
{
    // DTOs phục vụ nhận dữ liệu từ Client (Frontend)
    public class CreateUserDto
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "Student"; // "Student" hoặc "Teacher"
        public string? ClassCode { get; set; } // Dành cho học sinh nếu cần gán trực tiếp
    }

    public class CreateClassDto
    {
        public string ClassCode { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public int? TeacherId { get; set; }
        public List<int>? TeacherIds { get; set; } // Hỗ trợ 2-4 giáo viên cho 1 lớp
        public int? CreatedByAdminId { get; set; }
    }

    public class AssignTeacherDto
    {
        public int? TeacherId { get; set; }
        public List<int>? TeacherIds { get; set; } // Hỗ trợ gán 2-4 giáo viên
    }

    public class AddStudentToClassDto
    {
        public int StudentId { get; set; }
    }
}
