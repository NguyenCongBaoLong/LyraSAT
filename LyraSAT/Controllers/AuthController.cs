using LyraSAT.Data;
using LyraSAT.Models; // Đảm bảo namespace chứa model User của bạn
using Microsoft.AspNetCore.Identity; // Thêm thư viện để dùng PasswordHasher
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LyraSAT.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly LyraSatDbContext _context;
        private readonly PasswordHasher<User> _passwordHasher = new PasswordHasher<User>(); // Khởi tạo công cụ băm mật khẩu

        public AuthController(LyraSatDbContext context)
        {
            _context = context;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto model)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);

            if (user == null)
            {
                return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác!" });
            }

            // Kiểm tra mật khẩu thô người dùng nhập vào so với PasswordHash đã mã hóa trong DB
            var passwordVerificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, model.Password);

            if (passwordVerificationResult == PasswordVerificationResult.Failed)
            {
                return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác!" });
            }

            // --- Xử lý tính toán Streak ---
            var today = DateTime.UtcNow.Date;
            if (user.LastLoginDate == null)
            {
                // Đăng nhập lần đầu tiên
                user.StreakCount = 1;
                user.LastLoginDate = today;
            }
            else
            {
                var lastLogin = user.LastLoginDate.Value.Date;
                var diffDays = (today - lastLogin).Days;

                if (diffDays == 1)
                {
                    // Đăng nhập liên tiếp vào ngày hôm sau -> Tăng streak lên 1
                    user.StreakCount += 1;
                    user.LastLoginDate = today;
                }
                else if (diffDays > 1)
                {
                    // Bị ngắt quãng quá 1 ngày -> Reset về 1
                    user.StreakCount = 1;
                    user.LastLoginDate = today;
                }
                // Nếu diffDays == 0 (đăng nhập nhiều lần trong cùng một ngày) -> Giữ nguyên, không làm gì cả
            }

            // Lưu thay đổi vào Database
            await _context.SaveChangesAsync();

            // Trả về thông tin user kèm theo số streak mới nhất cho Frontend
            return Ok(new { message = "Đăng nhập thành công!", user });
        }

        // Bổ sung API Đăng ký tài khoản mới - Đã áp dụng Hash Password
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto model)
        {
            // Kiểm tra xem email đã tồn tại trong hệ thống chưa
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email);

            if (existingUser != null)
            {
                return BadRequest(new { message = "Email này đã được sử dụng!" });
            }

            // Tạo đối tượng User mới
            var newUser = new User
            {
                Email = model.Email,
                FullName = model.FullName,
                Role = string.IsNullOrEmpty(model.Role) ? "Student" : model.Role, // Mặc định là Student nếu không truyền
                ClassCode = model.ClassCode,
                TargetScore = 1500
            };

            // Băm mật khẩu trước khi lưu vào Database
            newUser.PasswordHash = _passwordHasher.HashPassword(newUser, model.Password);

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Đăng ký tài khoản thành công!",
                user = new
                {
                    newUser.Id,
                    newUser.Email,
                    newUser.FullName,
                    newUser.Role,
                    newUser.ClassCode
                }
            });
        }

        [HttpPost("update-profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto model)
        {
            var user = await _context.Users.FindAsync(model.UserId);
            if (user == null)
            {
                return NotFound(new { message = "Không tìm thấy người dùng!" });
            }

            user.Nickname = model.Nickname;
            user.School = model.School;

            // Ép kiểu an toàn cho Ngày sinh
            if (!string.IsNullOrEmpty(model.Dob) && DateTime.TryParse(model.Dob, out var parsedDob))
            {
                user.Dob = parsedDob;
            }
            else
            {
                user.Dob = null;
            }

            // Ép kiểu an toàn cho Ngày kiểm tra
            if (!string.IsNullOrEmpty(model.ExamDate) && DateTime.TryParse(model.ExamDate, out var parsedExamDate))
            {
                user.ExamDate = parsedExamDate;
            }

            user.Avatar = model.Avatar;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Cập nhật thành công!", user });
        }
    }

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