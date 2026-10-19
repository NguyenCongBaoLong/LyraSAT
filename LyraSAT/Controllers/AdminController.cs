using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity; // Thêm thư viện để dùng PasswordHasher
using LyraSAT.Data;
using LyraSAT.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LyraSAT.Controllers
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
        public int? CreatedByAdminId { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly LyraSatDbContext _context;
        private readonly PasswordHasher<User> _passwordHasher = new PasswordHasher<User>(); // Khởi tạo công cụ băm mật khẩu

        public AdminController(LyraSatDbContext context)
        {
            _context = context;
        }

        // 1. Tạo tài khoản mới (Học sinh hoặc Giáo viên) - Đã áp dụng Hash Password
        [HttpPost("users/create")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
        {
            try
            {
                if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
                {
                    return BadRequest(new { message = "Email này đã tồn tại trên hệ thống!" });
                }

                var newUser = new User
                {
                    FullName = dto.FullName,
                    Email = dto.Email,
                    Role = dto.Role,
                    ClassCode = dto.Role == "Student" ? dto.ClassCode : null,
                };

                // Băm mật khẩu trước khi lưu vào Database
                newUser.PasswordHash = _passwordHasher.HashPassword(newUser, dto.Password);

                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();

                return Ok(new { message = $"Tạo tài khoản {dto.Role} thành công!", userId = newUser.Id });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // 2. Lấy danh sách giáo viên (dùng để chọn khi tạo lớp học)
        [HttpGet("teachers")]
        public async Task<IActionResult> GetTeachers()
        {
            var teachers = await _context.Users
                .Where(u => u.Role == "Teacher")
                .Select(u => new { u.Id, u.FullName, u.Email })
                .ToListAsync();

            return Ok(teachers);
        }

        // 3. Tạo Lớp học mới (Dựa theo Model Class chuẩn của bạn)
        [HttpPost("classes/create")]
        public async Task<IActionResult> CreateClass([FromBody] CreateClassDto dto)
        {
            try
            {
                // Kiểm tra mã lớp đã tồn tại chưa
                if (await _context.Classes.AnyAsync(c => c.ClassCode == dto.ClassCode))
                {
                    return BadRequest(new { message = "Mã lớp học này đã tồn tại!" });
                }

                var newClass = new Class
                {
                    ClassCode = dto.ClassCode,
                    ClassName = dto.ClassName,
                    TeacherId = dto.TeacherId,
                    CreatedByAdminId = dto.CreatedByAdminId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Classes.Add(newClass);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Tạo lớp học mới thành công!", classId = newClass.Id });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // 4. Lấy danh sách toàn bộ lớp học kèm thông tin giáo viên phụ trách
        [HttpGet("classes")]
        public async Task<IActionResult> GetClasses()
        {
            try
            {
                var classes = await _context.Classes
                    .Include(c => c.Teacher)
                    .ToListAsync();

                var result = new List<object>();
                foreach (var c in classes)
                {
                    string teacherName = "Chưa phân công";
                    if (c.TeacherId != null)
                    {
                        var teacher = await _context.Users.FindAsync(c.TeacherId);
                        if (teacher != null)
                        {
                            teacherName = teacher.FullName;
                        }
                    }
                    else if (c.Teacher != null)
                    {
                        teacherName = c.Teacher.FullName;
                    }

                    result.Add(new
                    {
                        id = c.Id,
                        classCode = c.ClassCode,
                        className = c.ClassName,
                        teacherId = c.TeacherId,
                        teacherName = teacherName,
                        createdAt = c.CreatedAt
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        public class AssignTeacherDto
        {
            public int? TeacherId { get; set; }
        }

        // 1. API Phân công / Cập nhật giáo viên cho lớp học
        [HttpPut("classes/{id}/assign-teacher")]
        public async Task<IActionResult> AssignTeacher(int id, [FromBody] AssignTeacherDto dto)
        {
            try
            {
                var targetClass = await _context.Classes.FindAsync(id);
                if (targetClass == null)
                {
                    return NotFound(new { message = "Không tìm thấy lớp học!" });
                }

                targetClass.TeacherId = dto.TeacherId;
                await _context.SaveChangesAsync();

                return Ok(new { message = "Phân công giáo viên thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // 2. API Xóa lớp học
        [HttpDelete("classes/{id}")]
        public async Task<IActionResult> DeleteClass(int id)
        {
            try
            {
                var targetClass = await _context.Classes.FindAsync(id);
                if (targetClass == null)
                {
                    return NotFound(new { message = "Không tìm thấy lớp học!" });
                }

                _context.Classes.Remove(targetClass);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Xóa lớp học thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        public class AddStudentToClassDto
        {
            public int StudentId { get; set; }
        }

        // 1. Lấy danh sách học sinh thuộc một lớp cụ thể (dựa theo ClassCode)
        [HttpGet("classes/{classCode}/students")]
        public async Task<IActionResult> GetStudentsByClass(string classCode)
        {
            try
            {
                var students = await _context.Users
                    .Where(u => u.Role == "Student" && u.ClassCode == classCode)
                    .Select(u => new { u.Id, u.FullName, u.Email })
                    .ToListAsync();

                return Ok(students);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // 2. Lấy danh sách học sinh CHƯA thuộc lớp này (dùng cho Dropdown chọn thêm)
        [HttpGet("classes/{classCode}/available-students")]
        public async Task<IActionResult> GetAvailableStudents(string classCode)
        {
            try
            {
                var availableStudents = await _context.Users
                    .Where(u => u.Role == "Student" && u.ClassCode != classCode)
                    .Select(u => new { u.Id, u.FullName, u.Email, u.ClassCode })
                    .ToListAsync();

                return Ok(availableStudents);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // 3. Gán học sinh vào lớp
        [HttpPost("classes/{classCode}/add-student")]
        public async Task<IActionResult> AddStudentToClass(string classCode, [FromBody] AddStudentToClassDto dto)
        {
            try
            {
                var student = await _context.Users.FindAsync(dto.StudentId);
                if (student == null || student.Role != "Student")
                {
                    return NotFound(new { message = "Không tìm thấy học sinh!" });
                }

                student.ClassCode = classCode;
                await _context.SaveChangesAsync();

                return Ok(new { message = $"Đã thêm học sinh {student.FullName} vào lớp {classCode}!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }
    }
}