using LyraSAT.Data;
using LyraSAT.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace LyraSAT.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotificationsController : ControllerBase
    {
        private readonly LyraSatDbContext _context;

        public NotificationsController(LyraSatDbContext context)
        {
            _context = context;
        }

        // 1. Lấy danh sách thông báo của người dùng
        [HttpGet]
        public async Task<IActionResult> GetNotifications([FromQuery] int userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
        {
            try
            {
                var query = _context.Notifications
                    .AsNoTracking()
                    .Where(n => n.UserId == userId)
                    .OrderByDescending(n => n.CreatedAt);

                var totalCount = await query.CountAsync();
                var items = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(n => new
                    {
                        n.Id,
                        n.UserId,
                        n.ActorId,
                        ActorName = n.Actor != null ? n.Actor.FullName : "Hệ thống",
                        n.Type,
                        n.Title,
                        n.Content,
                        n.QuestionId,
                        n.TestId,
                        n.CommentId,
                        n.IsRead,
                        n.CreatedAt
                    })
                    .ToListAsync();

                return Ok(new
                {
                    totalCount,
                    page,
                    pageSize,
                    items
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi tải thông báo: " + ex.Message });
            }
        }

        // 2. Lấy số lượng thông báo chưa đọc
        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount([FromQuery] int userId)
        {
            try
            {
                var count = await _context.Notifications
                    .AsNoTracking()
                    .CountAsync(n => n.UserId == userId && !n.IsRead);

                return Ok(new { unreadCount = count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi đếm thông báo: " + ex.Message });
            }
        }

        // 3. Đánh dấu 1 thông báo đã đọc
        [HttpPut("{id}/read")]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            try
            {
                var notif = await _context.Notifications.FindAsync(id);
                if (notif == null)
                {
                    return NotFound(new { message = "Không tìm thấy thông báo." });
                }

                notif.IsRead = true;
                await _context.SaveChangesAsync();

                return Ok(new { message = "Đã đánh dấu đọc." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi cập nhật thông báo: " + ex.Message });
            }
        }

        // 4. Đánh dấu tất cả thông báo của người dùng đã đọc
        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllAsRead([FromQuery] int userId)
        {
            try
            {
                var unreadNotifs = await _context.Notifications
                    .Where(n => n.UserId == userId && !n.IsRead)
                    .ToListAsync();

                foreach (var notif in unreadNotifs)
                {
                    notif.IsRead = true;
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = "Đã đánh dấu đọc tất cả thông báo.", updatedCount = unreadNotifs.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi cập nhật tất cả thông báo: " + ex.Message });
            }
        }
    }
}
