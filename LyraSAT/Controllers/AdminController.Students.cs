using LyraSAT.Data;
using LyraSAT.DTOs;
using LyraSAT.Models;
using LyraSAT.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace LyraSAT.Controllers
{
    public partial class AdminController
    {
        // 1. Lấy danh sách học sinh thuộc một lớp cụ thể (dựa theo ClassCode)
        [HttpGet("classes/{classCode}/students")]
        public async Task<IActionResult> GetStudentsByClass(
            string classCode,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 8)
        {
            try
            {
                var lowerClassCode = ClassCodeHelper.Normalize(classCode).ToLower();

                var students = await _context.StudentClasses
                    .AsNoTracking()
                    .Where(sc => sc.Class != null && sc.Class.ClassCode.ToLower() == lowerClassCode && sc.User != null && sc.User.Role == "Student")
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
                var targetClass = await ClassCodeHelper.FindClassAsync(_context, classCode);
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

                var targetClass = await ClassCodeHelper.FindClassAsync(_context, classCode);
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
                    student.ClassCode = targetClass.ClassCode;
                }
                await _context.SaveChangesAsync();

                return Ok(new { message = $"Đã thêm học sinh {student.FullName} vào lớp {targetClass.ClassCode}!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }
    }
}
