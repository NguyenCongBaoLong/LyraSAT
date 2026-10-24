using LyraSAT.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        [HttpGet("teacher-classes")]
        public async Task<IActionResult> GetTeacherClasses([FromQuery] string email)
        {
            try
            {
                // 1. Tìm thông tin giáo viên dựa vào email đăng nhập
                var teacher = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Email == email && u.Role == "Teacher");

                if (teacher == null)
                {
                    return Ok(new List<object>()); // Không tìm thấy giáo viên -> trả về mảng rỗng
                }

                // 2. Lấy danh sách lớp học có TeacherId khớp với Id của giáo viên này
                var classes = await _context.Classes
                    .AsNoTracking()
                    .Where(c => c.TeacherId == teacher.Id)
                    .Select(c => new
                    {
                        code = c.ClassCode,
                        name = c.ClassName ?? c.ClassCode
                    })
                    .ToListAsync();

                return Ok(classes);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi lấy danh sách lớp học: " + ex.Message });
            }
        }
    }
}