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
        public List<int>? TeacherIds { get; set; } // Hỗ trợ 2-4 giáo viên cho 1 lớp
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

                if (newUser.Role == "Student" && !string.IsNullOrWhiteSpace(dto.ClassCode))
                {
                    var targetClass = await _context.Classes
                        .FirstOrDefaultAsync(c => c.ClassCode == dto.ClassCode);
                    if (targetClass != null)
                    {
                        _context.StudentClasses.Add(new StudentClass
                        {
                            UserId = newUser.Id,
                            ClassId = targetClass.Id
                        });
                        await _context.SaveChangesAsync();
                    }
                }

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
                .AsNoTracking()
                .Where(u => u.Role == "Teacher")
                .OrderBy(u => u.FullName)
                .Select(u => new { u.Id, u.FullName, u.Email })
                .ToListAsync();

            return Ok(teachers);
        }

        [HttpGet("teachers/paged")]
        public async Task<IActionResult> GetTeachersPaged(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 8)
        {
            var teachers = await _context.Users
                .AsNoTracking()
                .Where(u => u.Role == "Teacher")
                .OrderBy(u => u.FullName)
                .ThenBy(u => u.Id)
                .Select(u => new { u.Id, u.FullName, u.Email, u.IsActive })
                .ToPagedResultAsync(page, pageSize);

            return Ok(teachers);
        }

        // Bổ sung: Admin trực tiếp chuyển quyền Key chính cho giáo viên trong lớp
        [HttpPost("classes/{classCode}/transfer-lead/{teacherId}")]
        public async Task<IActionResult> AdminTransferLeadKey(string classCode, int teacherId)
        {
            try
            {
                var targetClass = await _context.Classes.FirstOrDefaultAsync(c => c.ClassCode == classCode);
                if (targetClass == null) return NotFound(new { message = "Không tìm thấy lớp học." });

                var targetTeacher = await _context.Users.FirstOrDefaultAsync(u => u.Id == teacherId && u.Role == "Teacher");
                if (targetTeacher == null) return NotFound(new { message = "Không tìm thấy giáo viên." });

                bool exists = await _context.ClassTeachers.AnyAsync(ct => ct.ClassCode == classCode && ct.TeacherId == teacherId);
                if (!exists)
                {
                    _context.ClassTeachers.Add(new ClassTeacher { ClassCode = classCode, TeacherId = teacherId });
                }

                targetClass.TeacherId = teacherId;
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = $"Đã trao Key chính lớp {classCode} cho cô/thầy {targetTeacher.FullName} thành công!",
                    leadTeacherId = teacherId
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi trao key: " + ex.Message });
            }
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

                var allTeacherIds = new List<int>();
                if (dto.TeacherIds != null && dto.TeacherIds.Any())
                {
                    allTeacherIds.AddRange(dto.TeacherIds);
                }
                else if (dto.TeacherId.HasValue)
                {
                    allTeacherIds.Add(dto.TeacherId.Value);
                }

                var primaryTeacherId = allTeacherIds.FirstOrDefault();

                var newClass = new Class
                {
                    ClassCode = dto.ClassCode,
                    ClassName = dto.ClassName,
                    TeacherId = primaryTeacherId > 0 ? primaryTeacherId : dto.TeacherId,
                    CreatedByAdminId = dto.CreatedByAdminId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Classes.Add(newClass);

                // Lưu danh sách giáo viên vào bảng ClassTeachers (2-4 giáo viên)
                foreach (var tId in allTeacherIds.Distinct())
                {
                    _context.ClassTeachers.Add(new ClassTeacher
                    {
                        ClassCode = dto.ClassCode,
                        TeacherId = tId,
                        AssignedAt = DateTime.UtcNow
                    });
                }

                await _context.SaveChangesAsync();

                return Ok(new { message = "Tạo lớp học mới thành công!", classId = newClass.Id });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // 4. Lấy danh sách toàn bộ lớp học kèm thông tin toàn bộ giáo viên phụ trách (hỗ trợ 2-4 giáo viên)
        [HttpGet("classes")]
        public async Task<IActionResult> GetClasses()
        {
            try
            {
                var classes = await _context.Classes
                    .AsNoTracking()
                    .OrderBy(c => c.ClassCode)
                    .Select(c => new
                    {
                        id = c.Id,
                        classCode = c.ClassCode,
                        className = c.ClassName,
                        teacherId = c.TeacherId,
                        teacherName = c.Teacher != null ? c.Teacher.FullName : "Chưa phân công",
                        createdAt = c.CreatedAt
                    })
                    .ToListAsync();

                var classCodes = classes.Select(c => c.classCode).ToList();
                var classTeachers = await _context.ClassTeachers
                    .AsNoTracking()
                    .Where(ct => classCodes.Contains(ct.ClassCode))
                    .Include(ct => ct.Teacher)
                    .ToListAsync();

                var result = classes.Select(c =>
                {
                    var teachersOfClass = classTeachers
                        .Where(ct => ct.ClassCode == c.classCode && ct.Teacher != null)
                        .Select(ct => new { id = ct.Teacher!.Id, fullName = ct.Teacher.FullName, email = ct.Teacher.Email })
                        .ToList();

                    if (!teachersOfClass.Any() && c.teacherId.HasValue && c.teacherName != "Chưa phân công")
                    {
                        teachersOfClass.Add(new { id = c.teacherId.Value, fullName = c.teacherName, email = "" });
                    }

                    return new
                    {
                        c.id,
                        c.classCode,
                        c.className,
                        c.teacherId,
                        c.teacherName,
                        teachers = teachersOfClass,
                        c.createdAt
                    };
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        [HttpGet("classes/paged")]
        public async Task<IActionResult> GetClassesPaged(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 8)
        {
            var paged = await _context.Classes
                .AsNoTracking()
                .OrderBy(c => c.ClassCode)
                .ThenBy(c => c.Id)
                .Select(c => new
                {
                    id = c.Id,
                    classCode = c.ClassCode,
                    className = c.ClassName,
                    teacherId = c.TeacherId,
                    teacherName = c.Teacher != null ? c.Teacher.FullName : "Chưa phân công",
                    createdAt = c.CreatedAt
                })
                .ToPagedResultAsync(page, pageSize);

            var classCodes = paged.Items.Select(c => c.classCode).ToList();
            var classTeachers = await _context.ClassTeachers
                .AsNoTracking()
                .Where(ct => classCodes.Contains(ct.ClassCode))
                .Include(ct => ct.Teacher)
                .ToListAsync();

            var mappedItems = paged.Items.Select(c =>
            {
                var teachersOfClass = classTeachers
                    .Where(ct => ct.ClassCode == c.classCode && ct.Teacher != null)
                    .Select(ct => new { id = ct.Teacher!.Id, fullName = ct.Teacher.FullName, email = ct.Teacher.Email })
                    .ToList();

                if (!teachersOfClass.Any() && c.teacherId.HasValue && c.teacherName != "Chưa phân công")
                {
                    teachersOfClass.Add(new { id = c.teacherId.Value, fullName = c.teacherName, email = "" });
                }

                return new
                {
                    c.id,
                    c.classCode,
                    c.className,
                    c.teacherId,
                    c.teacherName,
                    teachers = teachersOfClass,
                    c.createdAt
                };
            }).ToList();

            return Ok(new
            {
                paged.Page,
                paged.PageSize,
                paged.TotalCount,
                paged.TotalPages,
                items = mappedItems
            });
        }

        public class AssignTeacherDto
        {
            public int? TeacherId { get; set; }
            public List<int>? TeacherIds { get; set; } // Hỗ trợ gán 2-4 giáo viên
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

                var teacherIdsToAssign = new List<int>();
                if (dto.TeacherIds != null && dto.TeacherIds.Any())
                {
                    teacherIdsToAssign.AddRange(dto.TeacherIds);
                }
                else if (dto.TeacherId.HasValue)
                {
                    teacherIdsToAssign.Add(dto.TeacherId.Value);
                }

                targetClass.TeacherId = teacherIdsToAssign.FirstOrDefault() > 0 ? teacherIdsToAssign.First() : dto.TeacherId;

                // Xóa và cập nhật lại danh sách giáo viên trong ClassTeachers
                var existingClassTeachers = await _context.ClassTeachers
                    .Where(ct => ct.ClassCode == targetClass.ClassCode)
                    .ToListAsync();
                _context.ClassTeachers.RemoveRange(existingClassTeachers);

                foreach (var tId in teacherIdsToAssign.Distinct())
                {
                    _context.ClassTeachers.Add(new ClassTeacher
                    {
                        ClassCode = targetClass.ClassCode,
                        TeacherId = tId,
                        AssignedAt = DateTime.UtcNow
                    });
                }

                await _context.SaveChangesAsync();

                return Ok(new { message = $"Phân công {teacherIdsToAssign.Distinct().Count()} giáo viên cho lớp {targetClass.ClassCode} thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // Lấy danh sách toàn bộ giáo viên của 1 lớp học (hỗ trợ 2-4 giáo viên)
        [HttpGet("classes/{classCode}/teachers")]
        public async Task<IActionResult> GetClassTeachers(string classCode)
        {
            try
            {
                var targetClass = await _context.Classes.FirstOrDefaultAsync(c => c.ClassCode == classCode);
                if (targetClass == null) return NotFound(new { message = "Không tìm thấy lớp học." });

                var teacherIds = await _context.ClassTeachers
                    .Where(ct => ct.ClassCode == classCode)
                    .Select(ct => ct.TeacherId)
                    .ToListAsync();

                if (targetClass.TeacherId.HasValue && !teacherIds.Contains(targetClass.TeacherId.Value))
                {
                    teacherIds.Add(targetClass.TeacherId.Value);
                }

                var teachers = await _context.Users
                    .Where(u => teacherIds.Contains(u.Id))
                    .Select(u => new
                    {
                        u.Id,
                        u.FullName,
                        u.Email
                    })
                    .ToListAsync();

                return Ok(teachers);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // Thêm 1 giáo viên vào lớp học (Hỗ trợ lớp có 2-4 giáo viên)
        [HttpPost("classes/{classCode}/teachers")]
        public async Task<IActionResult> AddTeacherToClass(string classCode, [FromBody] AssignTeacherDto dto)
        {
            try
            {
                if (!dto.TeacherId.HasValue) return BadRequest(new { message = "Vui lòng chọn giáo viên cần thêm." });

                var targetClass = await _context.Classes.FirstOrDefaultAsync(c => c.ClassCode == classCode);
                if (targetClass == null) return NotFound(new { message = "Không tìm thấy lớp học." });

                bool exists = await _context.ClassTeachers.AnyAsync(ct => ct.ClassCode == classCode && ct.TeacherId == dto.TeacherId.Value);
                if (!exists)
                {
                    _context.ClassTeachers.Add(new ClassTeacher
                    {
                        ClassCode = classCode,
                        TeacherId = dto.TeacherId.Value,
                        AssignedAt = DateTime.UtcNow
                    });
                    if (!targetClass.TeacherId.HasValue) targetClass.TeacherId = dto.TeacherId.Value;
                    await _context.SaveChangesAsync();
                }

                return Ok(new { message = $"Đã thêm giáo viên vào lớp {classCode} thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // Gỡ 1 giáo viên khỏi lớp học
        [HttpDelete("classes/{classCode}/teachers/{teacherId}")]
        public async Task<IActionResult> RemoveTeacherFromClass(string classCode, int teacherId)
        {
            try
            {
                var ct = await _context.ClassTeachers.FirstOrDefaultAsync(x => x.ClassCode == classCode && x.TeacherId == teacherId);
                if (ct != null)
                {
                    _context.ClassTeachers.Remove(ct);
                }

                var targetClass = await _context.Classes.FirstOrDefaultAsync(c => c.ClassCode == classCode);
                if (targetClass != null && targetClass.TeacherId == teacherId)
                {
                    var remaining = await _context.ClassTeachers.FirstOrDefaultAsync(x => x.ClassCode == classCode && x.TeacherId != teacherId);
                    targetClass.TeacherId = remaining?.TeacherId;
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = $"Đã gỡ giáo viên khỏi lớp {classCode} thành công!" });
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
        public async Task<IActionResult> GetStudentsByClass(
            string classCode,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 8)
        {
            try
            {
                var students = await _context.StudentClasses
                    .AsNoTracking()
                    .Where(sc => sc.Class != null && sc.Class.ClassCode == classCode && sc.User != null && sc.User.Role == "Student")
                    .OrderBy(sc => sc.User!.FullName)
                    .ThenBy(sc => sc.UserId)
                    .Select(sc => new { sc.User!.Id, sc.User.FullName, sc.User.Email })
                    .ToPagedResultAsync(page, pageSize);

                return Ok(students);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // 2. Lấy danh sách học sinh CHƯA thuộc lớp này (dùng cho Dropdown chọn thêm)
        [HttpGet("classes/{classCode}/available-students")]
        public async Task<IActionResult> GetAvailableStudents(
            string classCode,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            try
            {
                var targetClass = await _context.Classes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.ClassCode == classCode);
                if (targetClass == null) return NotFound(new { message = "Không tìm thấy lớp học!" });

                var availableStudents = await _context.Users
                    .AsNoTracking()
                    .Where(u => u.Role == "Student" &&
                        !_context.StudentClasses.Any(sc => sc.UserId == u.Id && sc.ClassId == targetClass.Id))
                    .OrderBy(u => u.FullName)
                    .ThenBy(u => u.Id)
                    .Select(u => new { u.Id, u.FullName, u.Email, u.ClassCode })
                    .ToPagedResultAsync(page, pageSize);

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

                var targetClass = await _context.Classes
                    .FirstOrDefaultAsync(c => c.ClassCode == classCode);
                if (targetClass == null)
                {
                    return NotFound(new { message = "Không tìm thấy lớp học!" });
                }

                var alreadyAssigned = await _context.StudentClasses
                    .AnyAsync(sc => sc.UserId == student.Id && sc.ClassId == targetClass.Id);
                if (alreadyAssigned)
                {
                    return BadRequest(new { message = "Học sinh đã thuộc lớp này!" });
                }

                _context.StudentClasses.Add(new StudentClass
                {
                    UserId = student.Id,
                    ClassId = targetClass.Id
                });
                if (string.IsNullOrWhiteSpace(student.ClassCode))
                {
                    student.ClassCode = classCode;
                }
                await _context.SaveChangesAsync();

                return Ok(new { message = $"Đã thêm học sinh {student.FullName} vào lớp {classCode}!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // 4. Lấy danh sách toàn bộ học sinh có phân trang & tìm kiếm
        [HttpGet("students")]
        public async Task<IActionResult> GetAllStudents(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null,
            [FromQuery] string? classCode = null)
        {
            try
            {
                var query = _context.Users
                    .Where(u => u.Role == "Student");

                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim().ToLower();
                    query = query.Where(u => u.FullName.ToLower().Contains(search) || u.Email.ToLower().Contains(search));
                }

                if (!string.IsNullOrWhiteSpace(classCode))
                {
                    query = query.Where(u => u.ClassCode == classCode);
                }

                var result = await query
                    .OrderByDescending(u => u.Id)
                    .Select(u => new
                    {
                        u.Id,
                        u.FullName,
                        u.Email,
                        u.ClassCode,
                        u.IsActive,
                        u.TuitionStatus,
                        u.LastLoginDate
                    })
                    .ToPagedResultAsync(page, pageSize);

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // 5. Khóa hoặc Mở khóa đăng nhập tài khoản người dùng
        [HttpPost("users/{id}/toggle-status")]
        public async Task<IActionResult> ToggleUserStatus(int id)
        {
            try
            {
                var user = await _context.Users.FindAsync(id);
                if (user == null) return NotFound(new { message = "Không tìm thấy người dùng." });

                if (user.Role == "Admin") return BadRequest(new { message = "Không thể khóa tài khoản Quản trị viên." });

                user.IsActive = !user.IsActive;
                await _context.SaveChangesAsync();

                string statusText = user.IsActive ? "kích hoạt" : "khóa đăng nhập";
                return Ok(new
                {
                    message = $"Đã {statusText} tài khoản {user.FullName} ({user.Email}) thành công!",
                    isActive = user.IsActive
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // 6. Xóa vĩnh viễn tài khoản người dùng (Xóa toàn bộ bài nộp và bình luận liên quan)
        [HttpDelete("users/{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            try
            {
                var user = await _context.Users.FindAsync(id);
                if (user == null) return NotFound(new { message = "Không tìm thấy người dùng." });

                if (user.Role == "Admin") return BadRequest(new { message = "Không thể xóa tài khoản Quản trị viên." });

                // Dọn dẹp bài nộp và chi tiết bài nộp của học sinh
                var submissions = await _context.Submissions.Where(s => s.StudentId == id).ToListAsync();
                if (submissions.Any())
                {
                    var subIds = submissions.Select(s => s.Id).ToList();
                    var details = await _context.SubmissionDetails.Where(sd => subIds.Contains(sd.SubmissionId)).ToListAsync();
                    if (details.Any())
                    {
                        _context.SubmissionDetails.RemoveRange(details);
                    }
                    _context.Submissions.RemoveRange(submissions);
                }

                // Dọn dẹp bình luận và tương tác
                var comments = await _context.QuestionComments.Where(c => c.UserId == id).ToListAsync();
                if (comments.Any())
                {
                    var commentIds = comments.Select(c => c.Id).ToList();
                    var reactions = await _context.CommentReactions.Where(r => commentIds.Contains(r.CommentId) || r.UserId == id).ToListAsync();
                    if (reactions.Any())
                    {
                        _context.CommentReactions.RemoveRange(reactions);
                    }
                    _context.QuestionComments.RemoveRange(comments);
                }

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                return Ok(new { message = $"Đã xóa vĩnh viễn tài khoản {user.FullName} ({user.Email}) thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }
    }
}
