using LyraSAT.Data;
using LyraSAT.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LyraSAT.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ClassesController : ControllerBase
    {
        private readonly LyraSatDbContext _context;

        public ClassesController(LyraSatDbContext context)
        {
            _context = context;
        }

        // 1. Lấy danh sách lớp mà giáo viên được phân công (hỗ trợ nhiều giáo viên)
        [HttpGet("teacher-classes")]
        public async Task<IActionResult> GetTeacherClasses([FromQuery] string email)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(email))
                {
                    return BadRequest(new { message = "Email không được để trống." });
                }

                // 1. Tìm thông tin giáo viên dựa vào email đăng nhập
                var teacher = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Email == email && u.Role == "Teacher");

                if (teacher == null)
                {
                    return Ok(new List<object>()); // Không tìm thấy giáo viên -> trả về mảng rỗng
                }

                // 2. Lấy danh sách mã lớp từ ClassTeachers và Classes
                var assignedClassCodes = await _context.ClassTeachers
                    .AsNoTracking()
                    .Where(ct => ct.TeacherId == teacher.Id)
                    .Select(ct => ct.ClassCode)
                    .ToListAsync();

                var classes = await _context.Classes
                    .AsNoTracking()
                    .Where(c => c.TeacherId == teacher.Id || assignedClassCodes.Contains(c.ClassCode))
                    .Select(c => new
                    {
                        code = c.ClassCode,
                        name = c.ClassName ?? c.ClassCode,
                        isLead = c.TeacherId == teacher.Id
                    })
                    .Distinct()
                    .ToListAsync();

                return Ok(classes);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi lấy danh sách lớp học: " + ex.Message });
            }
        }

        // 2. Lấy thông tin đội ngũ giáo viên của một lớp (Giáo viên chính & Giáo viên phụ trách, tối đa 4 người)
        [HttpGet("{classCode}/team")]
        public async Task<IActionResult> GetClassTeam(string classCode, [FromQuery] int? userId = null)
        {
            try
            {
                var targetClass = await _context.Classes.FirstOrDefaultAsync(c => c.ClassCode == classCode);
                if (targetClass == null)
                {
                    return NotFound(new { message = "Không tìm thấy lớp học." });
                }

                // Đồng bộ nếu targetClass.TeacherId chưa có trong ClassTeachers
                if (targetClass.TeacherId.HasValue)
                {
                    bool existsInCt = await _context.ClassTeachers.AnyAsync(ct => ct.ClassCode == classCode && ct.TeacherId == targetClass.TeacherId.Value);
                    if (!existsInCt)
                    {
                        _context.ClassTeachers.Add(new ClassTeacher
                        {
                            ClassCode = classCode,
                            TeacherId = targetClass.TeacherId.Value,
                            AssignedAt = DateTime.UtcNow
                        });
                        await _context.SaveChangesAsync();
                    }
                }

                // Danh sách giáo viên hiện tại của lớp
                var assignments = await _context.ClassTeachers
                    .Where(ct => ct.ClassCode == classCode)
                    .ToListAsync();

                var teacherIds = assignments.Select(ct => ct.TeacherId).Distinct().ToList();
                if (targetClass.TeacherId.HasValue && !teacherIds.Contains(targetClass.TeacherId.Value))
                {
                    teacherIds.Add(targetClass.TeacherId.Value);
                }

                var teachersData = await _context.Users
                    .Where(u => teacherIds.Contains(u.Id))
                    .Select(u => new
                    {
                        u.Id,
                        u.FullName,
                        u.Email
                    })
                    .ToListAsync();

                var teachersList = teachersData.Select(u =>
                {
                    var assign = assignments.FirstOrDefault(a => a.TeacherId == u.Id);
                    return new
                    {
                        id = u.Id,
                        fullName = u.FullName,
                        email = u.Email,
                        isLead = targetClass.TeacherId.HasValue && targetClass.TeacherId.Value == u.Id,
                        assignedAt = assign?.AssignedAt
                    };
                }).OrderByDescending(t => t.isLead).ThenBy(t => t.fullName).ToList();

                // Xác định quyền của người gửi yêu cầu
                bool isLead = false;
                bool isAdmin = false;
                if (userId.HasValue)
                {
                    var currentUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value);
                    isAdmin = currentUser?.Role == "Admin";
                    isLead = (targetClass.TeacherId.HasValue && targetClass.TeacherId.Value == userId.Value);
                }

                // Lấy danh sách giáo viên có thể thêm vào lớp (chưa thuộc lớp này)
                var availableTeachers = await _context.Users
                    .Where(u => u.Role == "Teacher" && u.IsActive && !teacherIds.Contains(u.Id))
                    .Select(u => new
                    {
                        id = u.Id,
                        fullName = u.FullName,
                        email = u.Email
                    })
                    .OrderBy(u => u.fullName)
                    .ToListAsync();

                return Ok(new
                {
                    classCode = targetClass.ClassCode,
                    className = targetClass.ClassName ?? targetClass.ClassCode,
                    leadTeacherId = targetClass.TeacherId,
                    isCurrentUserLead = isLead,
                    canManage = isLead || isAdmin,
                    maxTeachers = 4,
                    currentTeacherCount = teachersList.Count,
                    teachers = teachersList,
                    availableTeachers = availableTeachers
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi lấy thông tin đội ngũ giáo viên: " + ex.Message });
            }
        }

        // 3. Thêm giáo viên vào lớp học (Chỉ Giáo viên chính hoặc Admin có quyền, tối đa 4 giáo viên)
        [HttpPost("{classCode}/team/add")]
        public async Task<IActionResult> AddTeacherToTeam(string classCode, [FromBody] AddTeacherRequest req)
        {
            try
            {
                var targetClass = await _context.Classes.FirstOrDefaultAsync(c => c.ClassCode == classCode);
                if (targetClass == null) return NotFound(new { message = "Không tìm thấy lớp học." });

                // Kiểm tra quyền: phải là Admin hoặc Lead Teacher của lớp
                var requester = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.RequestUserId);
                bool isAdmin = requester?.Role == "Admin";
                bool isLead = targetClass.TeacherId.HasValue && targetClass.TeacherId.Value == req.RequestUserId;

                if (!isAdmin && !isLead)
                {
                    return StatusCode(403, new { message = "Chỉ Giáo viên chính (nắm giữ key chính) hoặc Admin mới có quyền thêm giáo viên vào lớp." });
                }

                // Kiểm tra số lượng giáo viên hiện tại (tối đa 4)
                var currentCount = await _context.ClassTeachers.CountAsync(ct => ct.ClassCode == classCode);
                if (currentCount >= 4)
                {
                    return BadRequest(new { message = "Lớp học đã đạt giới hạn tối đa 4 giáo viên. Không thể thêm nữa." });
                }

                // Kiểm tra giáo viên được thêm
                var teacherToAdd = await _context.Users.FirstOrDefaultAsync(u => u.Id == req.TeacherId && u.Role == "Teacher");
                if (teacherToAdd == null)
                {
                    return BadRequest(new { message = "Không tìm thấy thông tin giáo viên được chọn." });
                }

                // Kiểm tra xem đã có trong lớp chưa
                bool alreadyInClass = await _context.ClassTeachers.AnyAsync(ct => ct.ClassCode == classCode && ct.TeacherId == req.TeacherId);
                if (alreadyInClass)
                {
                    return BadRequest(new { message = "Giáo viên này đã thuộc lớp học rồi." });
                }

                // Thêm vào bảng ClassTeachers
                _context.ClassTeachers.Add(new ClassTeacher
                {
                    ClassCode = classCode,
                    TeacherId = req.TeacherId,
                    AssignedAt = DateTime.UtcNow
                });

                // Nếu lớp chưa có giáo viên chính nào, đặt giáo viên này làm chính
                if (!targetClass.TeacherId.HasValue)
                {
                    targetClass.TeacherId = req.TeacherId;
                }

                await _context.SaveChangesAsync();

                return Ok(new { message = $"Đã thêm cô/thầy {teacherToAdd.FullName} vào lớp {classCode} thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi thêm giáo viên: " + ex.Message });
            }
        }

        // 4. Loại trừ giáo viên hoặc Rời khỏi lớp học
        [HttpPost("{classCode}/team/remove")]
        public async Task<IActionResult> RemoveTeacherFromTeam(string classCode, [FromBody] RemoveTeacherRequest req)
        {
            try
            {
                var targetClass = await _context.Classes.FirstOrDefaultAsync(c => c.ClassCode == classCode);
                if (targetClass == null) return NotFound(new { message = "Không tìm thấy lớp học." });

                var requester = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.RequestUserId);
                bool isAdmin = requester?.Role == "Admin";
                bool isLead = targetClass.TeacherId.HasValue && targetClass.TeacherId.Value == req.RequestUserId;

                bool isSelfLeaving = (req.TeacherId == req.RequestUserId);

                if (isSelfLeaving)
                {
                    // Trường hợp 1: Tự rời khỏi lớp
                    // Nếu người tự rời là Giáo viên chính (nắm key)
                    if (isLead)
                    {
                        var otherTeachersCount = await _context.ClassTeachers.CountAsync(ct => ct.ClassCode == classCode && ct.TeacherId != req.RequestUserId);
                        if (otherTeachersCount > 0)
                        {
                            return BadRequest(new
                            {
                                message = "Bạn đang là Giáo viên chính (nắm giữ key chính của lớp). Vui lòng dùng nút 'Chuyển key chính' cho giáo viên khác trong lớp trước khi rời khỏi lớp!"
                            });
                        }
                        else
                        {
                            // Là giáo viên duy nhất trong lớp -> cho phép rời, lớp sẽ không còn giáo viên
                            targetClass.TeacherId = null;
                        }
                    }

                    // Xóa khỏi ClassTeachers
                    var ct = await _context.ClassTeachers.FirstOrDefaultAsync(x => x.ClassCode == classCode && x.TeacherId == req.TeacherId);
                    if (ct != null) _context.ClassTeachers.Remove(ct);

                    await _context.SaveChangesAsync();
                    return Ok(new { message = $"Bạn đã rời khỏi lớp {classCode} thành công." });
                }
                else
                {
                    // Trường hợp 2: Giáo viên chính hoặc Admin loại trừ một giáo viên khác khỏi lớp
                    if (!isAdmin && !isLead)
                    {
                        return StatusCode(403, new { message = "Chỉ Giáo viên chính hoặc Admin mới có quyền loại trừ giáo viên khỏi lớp." });
                    }

                    // Không được loại trừ Giáo viên chính
                    if (targetClass.TeacherId.HasValue && targetClass.TeacherId.Value == req.TeacherId)
                    {
                        return BadRequest(new { message = "Không thể loại trừ Giáo viên chính của lớp. Hãy truyền key chính cho người khác trước." });
                    }

                    var ct = await _context.ClassTeachers.FirstOrDefaultAsync(x => x.ClassCode == classCode && x.TeacherId == req.TeacherId);
                    if (ct != null)
                    {
                        _context.ClassTeachers.Remove(ct);
                        await _context.SaveChangesAsync();
                    }

                    return Ok(new { message = "Đã loại trừ giáo viên khỏi lớp thành công!" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi xử lý gỡ/rời giáo viên: " + ex.Message });
            }
        }

        // 5. Truyền key chính (Chuyển quyền Giáo viên chính) cho giáo viên khác trong lớp
        [HttpPost("{classCode}/team/transfer-key")]
        public async Task<IActionResult> TransferLeadKey(string classCode, [FromBody] TransferKeyRequest req)
        {
            try
            {
                var targetClass = await _context.Classes.FirstOrDefaultAsync(c => c.ClassCode == classCode);
                if (targetClass == null) return NotFound(new { message = "Không tìm thấy lớp học." });

                var requester = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.RequestUserId);
                bool isAdmin = requester?.Role == "Admin";
                bool isLead = targetClass.TeacherId.HasValue && targetClass.TeacherId.Value == req.RequestUserId;

                if (!isAdmin && !isLead)
                {
                    return StatusCode(403, new { message = "Chỉ Giáo viên chính hiện tại hoặc Admin mới có quyền truyền key chính của lớp." });
                }

                if (req.NewLeadTeacherId == targetClass.TeacherId)
                {
                    return BadRequest(new { message = "Giáo viên này hiện tại đã là Giáo viên chính rồi." });
                }

                // Kiểm tra xem giáo viên mới có đang phụ trách lớp này không
                bool isInClass = await _context.ClassTeachers.AnyAsync(ct => ct.ClassCode == classCode && ct.TeacherId == req.NewLeadTeacherId);
                if (!isInClass)
                {
                    return BadRequest(new { message = "Giáo viên được chọn nhận key phải đang là thành viên phụ trách lớp này." });
                }

                var newLeadUser = await _context.Users.FindAsync(req.NewLeadTeacherId);
                if (newLeadUser == null)
                {
                    return BadRequest(new { message = "Không tìm thấy thông tin giáo viên nhận key." });
                }

                // Cập nhật Giáo viên chính mới
                targetClass.TeacherId = req.NewLeadTeacherId;

                // Đảm bảo cả người cũ và người mới đều có bản ghi trong ClassTeachers
                bool oldInCt = await _context.ClassTeachers.AnyAsync(ct => ct.ClassCode == classCode && ct.TeacherId == req.RequestUserId);
                if (!oldInCt && req.RequestUserId > 0)
                {
                    _context.ClassTeachers.Add(new ClassTeacher
                    {
                        ClassCode = classCode,
                        TeacherId = req.RequestUserId,
                        AssignedAt = DateTime.UtcNow
                    });
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = $"Đã truyền key chính thành công cho cô/thầy {newLeadUser.FullName}! Từ bây giờ {newLeadUser.FullName} sẽ là Giáo viên chính của lớp {classCode}."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi truyền key chính: " + ex.Message });
            }
        }
    }

    public class AddTeacherRequest
    {
        public int TeacherId { get; set; }
        public int RequestUserId { get; set; }
    }

    public class RemoveTeacherRequest
    {
        public int TeacherId { get; set; }
        public int RequestUserId { get; set; }
    }

    public class TransferKeyRequest
    {
        public int NewLeadTeacherId { get; set; }
        public int RequestUserId { get; set; }
    }
}
