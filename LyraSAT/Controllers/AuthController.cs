using LyraSAT.Data;
using LyraSAT.DTOs;
using LyraSAT.Models; // Đảm bảo namespace chứa model User của bạn
using LyraSAT.Services;
using Microsoft.AspNetCore.Identity; // Thêm thư viện để dùng PasswordHasher
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

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
            var email = (model.Email ?? string.Empty).Trim().ToLower();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);

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

            // Kiểm tra trạng thái tài khoản có bị khóa không
            if (!user.IsActive)
            {
                return StatusCode(403, new { message = "Tài khoản của bạn đã bị khóa hoặc hết hạn truy cập. Vui lòng liên hệ ban quản trị để được hỗ trợ!" });
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
            return Ok(new { message = "Đăng nhập thành công!", user = await UserProfileMapper.ToSafeUserAsync(_context, user) });
        }

        // Bổ sung API Đăng ký tài khoản mới - Đã áp dụng Hash Password
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto model)
        {
            var email = (model.Email ?? string.Empty).Trim().ToLower();
            if (email.Length == 0)
            {
                return BadRequest(new { message = "Email không được để trống!" });
            }

            // Kiểm tra xem email đã tồn tại trong hệ thống chưa (không phân biệt hoa/thường)
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email);

            if (existingUser != null)
            {
                return BadRequest(new { message = "Email này đã được sử dụng!" });
            }

            var role = string.IsNullOrEmpty(model.Role) ? "Student" : model.Role;
            var classCode = ClassCodeHelper.Normalize(model.ClassCode);

            // Học sinh đăng ký phải chọn lớp có thật, nếu không sẽ sinh ra mã lớp mồ côi
            Class? targetClass = null;
            if (role == "Student" && classCode.Length > 0)
            {
                targetClass = await ClassCodeHelper.FindClassAsync(_context, classCode);
                if (targetClass == null)
                {
                    return BadRequest(new { message = $"Mã lớp '{classCode}' không tồn tại. Vui lòng kiểm tra lại." });
                }
                classCode = targetClass.ClassCode;
            }

            // Tạo đối tượng User mới
            var newUser = new User
            {
                Email = email,
                FullName = model.FullName,
                Role = role,
                ClassCode = classCode.Length > 0 ? classCode : null,
                TargetScore = 1500
            };

            // Băm mật khẩu trước khi lưu vào Database
            newUser.PasswordHash = _passwordHasher.HashPassword(newUser, model.Password);

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            if (newUser.Role == "Student" && targetClass != null)
            {
                _context.StudentClasses.Add(new StudentClass
                {
                    UserId = newUser.Id,
                    ClassId = targetClass.Id
                });
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                message = "Đăng ký tài khoản thành công!",
                user = await UserProfileMapper.ToSafeUserAsync(_context, newUser)
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
            if (!string.IsNullOrEmpty(model.Dob) && DateTime.TryParse(
                model.Dob,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedDob))
            {
                user.Dob = parsedDob;
            }
            else
            {
                user.Dob = null;
            }

            // Ép kiểu an toàn cho Ngày kiểm tra
            if (!string.IsNullOrEmpty(model.ExamDate) && DateTime.TryParse(
                model.ExamDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedExamDate))
            {
                user.ExamDate = parsedExamDate;
            }

            user.Avatar = model.Avatar;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Cập nhật thành công!", user = await UserProfileMapper.ToSafeUserAsync(_context, user) });
        }
    }
}