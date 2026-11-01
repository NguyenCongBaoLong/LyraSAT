using LyraSAT.Data;
using LyraSAT.DTOs;
using LyraSAT.Models;
using LyraSAT.Services;
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
        // Bổ sung: Admin trực tiếp chuyển quyền Key chính cho giáo viên trong lớp
        [HttpPost("classes/{classCode}/transfer-lead/{teacherId}")]
        public async Task<IActionResult> AdminTransferLeadKey(string classCode, int teacherId)
        {
            try
            {
                var targetClass = await _context.Classes.FirstOrDefaultAsync(c => c.ClassCode.ToLower() == classCode.Trim().ToLower());
                if (targetClass == null) return NotFound(new { message = "Không tìm thấy lớp học." });

                var targetTeacher = await _context.Users.FirstOrDefaultAsync(u => u.Id == teacherId && u.Role == "Teacher");
                if (targetTeacher == null) return NotFound(new { message = "Không tìm thấy giáo viên." });

                bool exists = await _context.ClassTeachers.AnyAsync(ct => ct.ClassCode.ToLower() == classCode.Trim().ToLower() && ct.TeacherId == teacherId);
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
                var classCode = ClassCodeHelper.Normalize(dto.ClassCode);
                if (classCode.Length == 0)
                {
                    return BadRequest(new { message = "Mã lớp không được để trống." });
                }

                // Kiểm tra mã lớp đã tồn tại chưa (không phân biệt hoa/thường)
                if (await ClassCodeHelper.ClassExistsAsync(_context, classCode))
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
                    ClassCode = classCode,
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
                        ClassCode = classCode,
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

                var classCodes = classes.Select(c => ClassCodeHelper.Normalize(c.classCode).ToLower()).ToList();
                var classTeachers = await _context.ClassTeachers
                    .AsNoTracking()
                    .Where(ct => classCodes.Contains(ct.ClassCode.ToLower()))
                    .Include(ct => ct.Teacher)
                    .ToListAsync();

                var result = classes.Select(c =>
                {
                    var teachersOfClass = classTeachers
                        .Where(ct => ClassCodeHelper.SameCode(ct.ClassCode, c.classCode) && ct.Teacher != null)
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

            var classCodes = paged.Items.Select(c => ClassCodeHelper.Normalize(c.classCode).ToLower()).ToList();
            var classTeachers = await _context.ClassTeachers
                .AsNoTracking()
                .Where(ct => classCodes.Contains(ct.ClassCode.ToLower()))
                .Include(ct => ct.Teacher)
                .ToListAsync();

            var mappedItems = paged.Items.Select(c =>
            {
                var teachersOfClass = classTeachers
                    .Where(ct => ClassCodeHelper.SameCode(ct.ClassCode, c.classCode) && ct.Teacher != null)
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
                    .Where(ct => ct.ClassCode.ToLower() == targetClass.ClassCode.Trim().ToLower())
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
                var targetClass = await _context.Classes.FirstOrDefaultAsync(c => c.ClassCode.ToLower() == classCode.Trim().ToLower());
                if (targetClass == null) return NotFound(new { message = "Không tìm thấy lớp học." });

                var teacherIds = await _context.ClassTeachers
                    .Where(ct => ct.ClassCode.ToLower() == classCode.Trim().ToLower())
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

                var targetClass = await _context.Classes.FirstOrDefaultAsync(c => c.ClassCode.ToLower() == classCode.Trim().ToLower());
                if (targetClass == null) return NotFound(new { message = "Không tìm thấy lớp học." });

                bool exists = await _context.ClassTeachers.AnyAsync(ct => ct.ClassCode.ToLower() == classCode.Trim().ToLower() && ct.TeacherId == dto.TeacherId.Value);
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
                var ct = await _context.ClassTeachers.FirstOrDefaultAsync(x => x.ClassCode.ToLower() == classCode.Trim().ToLower() && x.TeacherId == teacherId);
                if (ct != null)
                {
                    _context.ClassTeachers.Remove(ct);
                }

                var targetClass = await _context.Classes.FirstOrDefaultAsync(c => c.ClassCode.ToLower() == classCode.Trim().ToLower());
                if (targetClass != null && targetClass.TeacherId == teacherId)
                {
                    var remaining = await _context.ClassTeachers.FirstOrDefaultAsync(x => x.ClassCode.ToLower() == classCode.Trim().ToLower() && x.TeacherId != teacherId);
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
        // Xoá lớp phải dọn cả ClassTeachers (bảng này nối bằng mã lớp dạng chuỗi, không có FK)
        // và phải chặn khi lớp còn đề thi / bộ từ vựng, nếu không sẽ sinh ra "lớp ma" như f2609.
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

                var classCode = ClassCodeHelper.Normalize(targetClass.ClassCode);
                var lower = classCode.ToLower();

                var testCount = await _context.Tests.CountAsync(t => t.ClassCode != null && t.ClassCode.ToLower() == lower);
                var vocabCount = await _context.Vocabularies.CountAsync(v => v.ClassCode != null && v.ClassCode.ToLower() == lower);

                if (testCount > 0 || vocabCount > 0)
                {
                    return BadRequest(new
                    {
                        message = $"Không thể xóa lớp {classCode} vì vẫn còn {testCount} bài test và {vocabCount} bộ từ vựng. " +
                                  "Hãy xóa hoặc chuyển dữ liệu sang lớp khác trước."
                    });
                }

                // Dọn phân công giáo viên của lớp (nếu không sẽ còn row mồ côi trỏ tới mã lớp đã xoá)
                var classTeachers = await _context.ClassTeachers
                    .Where(ct => ct.ClassCode.ToLower() == lower)
                    .ToListAsync();
                if (classTeachers.Any())
                {
                    _context.ClassTeachers.RemoveRange(classTeachers);
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
    }
}
