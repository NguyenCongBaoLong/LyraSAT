using LyraSAT.Data;
using LyraSAT.DTOs;
using LyraSAT.Models;
using LyraSAT.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LyraSAT.Controllers
{
    public partial class AdminController
    {
        // 1. Tạo tài khoản mới (Học sinh hoặc Giáo viên) - Đã áp dụng Hash Password
        [HttpPost("users/create")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
        {
            try
            {
                var email = (dto.Email ?? string.Empty).Trim().ToLower();
                if (email.Length == 0)
                {
                    return BadRequest(new { message = "Email không được để trống." });
                }

                if (await _context.Users.AnyAsync(u => u.Email.ToLower() == email))
                {
                    return BadRequest(new { message = "Email này đã tồn tại trên hệ thống!" });
                }

                var classCode = ClassCodeHelper.Normalize(dto.ClassCode);

                // Học sinh phải thuộc một lớp có thật, nếu không sẽ sinh ra mã lớp mồ côi
                Class? targetClass = null;
                if (dto.Role == "Student" && classCode.Length > 0)
                {
                    targetClass = await ClassCodeHelper.FindClassAsync(_context, classCode);
                    if (targetClass == null)
                    {
                        return BadRequest(new { message = $"Mã lớp '{classCode}' không tồn tại. Vui lòng chọn lại lớp." });
                    }
                    classCode = targetClass.ClassCode;
                }

                var newUser = new User
                {
                    FullName = dto.FullName,
                    Email = email,
                    Role = dto.Role,
                    ClassCode = dto.Role == "Student" && classCode.Length > 0 ? classCode : null,
                };

                // Băm mật khẩu trước khi lưu vào Database
                newUser.PasswordHash = _passwordHasher.HashPassword(newUser, dto.Password);

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
                    var lowerClassCode = ClassCodeHelper.Normalize(classCode).ToLower();
                    query = query.Where(u => u.ClassCode != null && u.ClassCode.ToLower() == lowerClassCode);
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
                // Gồm cả các trả lời của người khác nhắm vào bình luận của user này:
                // FK ParentCommentId là Restrict nên nếu bỏ sót, lệnh xoá sẽ ném lỗi FK.
                var rootCommentIds = await _context.QuestionComments
                    .Where(c => c.UserId == id)
                    .Select(c => c.Id)
                    .ToListAsync();

                if (rootCommentIds.Any())
                {
                    var toDeleteIds = new HashSet<int>(rootCommentIds);
                    bool foundMore = true;
                    while (foundMore)
                    {
                        var currentIds = toDeleteIds.ToList();
                        var childIds = await _context.QuestionComments
                            .Where(c => c.ParentCommentId.HasValue && currentIds.Contains(c.ParentCommentId.Value))
                            .Select(c => c.Id)
                            .ToListAsync();

                        foundMore = false;
                        foreach (var childId in childIds)
                        {
                            if (toDeleteIds.Add(childId)) foundMore = true;
                        }
                    }

                    var ids = toDeleteIds.ToList();

                    var reactions = await _context.CommentReactions
                        .Where(r => ids.Contains(r.CommentId) || r.UserId == id)
                        .ToListAsync();
                    if (reactions.Any())
                    {
                        _context.CommentReactions.RemoveRange(reactions);
                    }

                    // Thông báo trỏ tới bình luận sắp xoá cũng phải dọn, nếu không chuông sẽ còn thông báo mồ côi
                    var commentNotifications = await _context.Notifications
                        .Where(n => n.CommentId.HasValue && ids.Contains(n.CommentId.Value))
                        .ToListAsync();
                    if (commentNotifications.Any())
                    {
                        _context.Notifications.RemoveRange(commentNotifications);
                    }

                    var comments = await _context.QuestionComments
                        .Where(c => ids.Contains(c.Id))
                        .ToListAsync();

                    // Cắt liên kết cha-trước để không vướng FK Restrict khi xoá
                    foreach (var comment in comments)
                    {
                        comment.ParentCommentId = null;
                    }
                    await _context.SaveChangesAsync();

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
