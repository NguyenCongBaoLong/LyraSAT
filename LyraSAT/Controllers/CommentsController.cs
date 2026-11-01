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
    [Route("api/[controller]")]
    [ApiController]
    public class CommentsController : ControllerBase
    {
        private readonly LyraSatDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly ILogger<CommentsController> _logger;

        public CommentsController(
            LyraSatDbContext context,
            INotificationService notificationService,
            ILogger<CommentsController> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _logger = logger;
        }

        // 1. Lấy toàn bộ bình luận của 1 câu hỏi theo dạng cây (Tree)
        [HttpGet("question/{questionId}")]
        [HttpGet("/api/questions/{questionId}/comments")]
        public async Task<IActionResult> GetCommentsByQuestion(int questionId, [FromQuery] int? userId = null)
        {
            try
            {
                var comments = await _context.QuestionComments
                    .AsNoTracking()
                    .Where(c => c.QuestionId == questionId)
                    .Include(c => c.User)
                    .Include(c => c.Reactions)
                    .OrderBy(c => c.CreatedAt)
                    .ToListAsync();

                var currentUserId = userId ?? 0;

                // Lọc và chuyển đổi sang DTO
                var dtoList = comments.Select(c => new CommentItemDto
                {
                    Id = c.Id,
                    QuestionId = c.QuestionId,
                    UserId = c.UserId,
                    UserName = c.User?.FullName ?? "Người dùng",
                    UserRole = c.User?.Role ?? "Student",
                    Content = c.IsDeleted ? "Bình luận này đã bị xóa." : c.Content,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    IsEdited = c.IsEdited,
                    ParentCommentId = c.ParentCommentId,
                    IsPinned = c.IsPinned,
                    PinnedAt = c.PinnedAt,
                    AttachmentUrl = c.IsDeleted ? null : c.AttachmentUrl,
                    AttachmentType = c.IsDeleted ? null : c.AttachmentType,
                    AudioDuration = c.IsDeleted ? null : c.AudioDuration,
                    IsDeleted = c.IsDeleted,
                    LikesCount = c.Reactions.Count,
                    IsLikedByMe = currentUserId > 0 && c.Reactions.Any(r => r.UserId == currentUserId),
                    Replies = new List<CommentItemDto>()
                }).ToList();

                // Tạo cấu trúc phân nhánh cây (Thread / Replies)
                var lookup = dtoList.ToLookup(c => c.ParentCommentId);
                var rootComments = lookup[null].ToList();

                foreach (var root in rootComments)
                {
                    root.Replies = lookup[root.Id].OrderBy(r => r.CreatedAt).ToList();
                }

                // Sắp xếp: Comment được ghim (IsPinned) lên đầu tiên, sau đó theo thời gian mới nhất
                var orderedRoots = rootComments
                    .OrderByDescending(c => c.IsPinned)
                    .ThenBy(c => c.CreatedAt)
                    .ToList();

                return Ok(orderedRoots);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi tải bình luận: " + ex.Message });
            }
        }

        // 2. Tạo bình luận mới hoặc phản hồi (Hỗ trợ cả POST /api/comments và POST /api/questions/{questionId}/comments)
        [HttpPost]
        [HttpPost("/api/questions/{questionId}/comments")]
        [HttpPost("questions/{questionId}/comments")]
        public async Task<IActionResult> CreateComment([FromBody] CreateCommentDto dto, [FromRoute] int? questionId = null)
        {
            try
            {
                if (questionId.HasValue && dto.QuestionId == 0)
                {
                    dto.QuestionId = questionId.Value;
                }

                if (!dto.ParentCommentId.HasValue && dto.ParentId.HasValue)
                {
                    dto.ParentCommentId = dto.ParentId.Value;
                }

                if (string.IsNullOrWhiteSpace(dto.Content) && string.IsNullOrEmpty(dto.AttachmentUrl))
                {
                    return BadRequest(new { message = "Nội dung bình luận hoặc tệp đính kèm không được để trống." });
                }

                var user = await _context.Users.FindAsync(dto.UserId);
                if (user == null)
                {
                    return NotFound(new { message = "Không tìm thấy thông tin người dùng." });
                }

                var question = await _context.Questions
                    .Include(q => q.Test)
                    .FirstOrDefaultAsync(q => q.Id == dto.QuestionId);

                if (question == null)
                {
                    return NotFound(new { message = "Không tìm thấy câu hỏi." });
                }

                var newComment = new QuestionComment
                {
                    QuestionId = dto.QuestionId,
                    UserId = dto.UserId,
                    ParentCommentId = dto.ParentCommentId,
                    Content = dto.Content ?? string.Empty,
                    AttachmentUrl = dto.AttachmentUrl,
                    AttachmentType = dto.AttachmentType,
                    AudioDuration = dto.AudioDuration,
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                };

                _context.QuestionComments.Add(newComment);
                await _context.SaveChangesAsync();

                // --- Xử lý Thông báo (Notification) ---
                try
                {
                    await _notificationService.NotifyCommentCreatedAsync(question, user, dto, newComment.Id);
                }
                catch (Exception notifEx)
                {
                    // Lỗi thông báo không chặn luồng comment chính, nhưng phải ghi log để còn truy vết
                    _logger.LogError(notifEx, "Lỗi gửi thông báo cho bình luận {CommentId} (câu hỏi {QuestionId})", newComment.Id, dto.QuestionId);
                }

                return Ok(new
                {
                    message = "Thêm bình luận thành công!",
                    comment = new CommentItemDto
                    {
                        Id = newComment.Id,
                        QuestionId = newComment.QuestionId,
                        UserId = user.Id,
                        UserName = user.FullName,
                        UserRole = user.Role,
                        Content = newComment.Content,
                        CreatedAt = newComment.CreatedAt,
                        ParentCommentId = newComment.ParentCommentId,
                        AttachmentUrl = newComment.AttachmentUrl,
                        AttachmentType = newComment.AttachmentType,
                        AudioDuration = newComment.AudioDuration,
                        IsPinned = false,
                        LikesCount = 0,
                        IsLikedByMe = false
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi tạo bình luận: " + ex.Message });
            }
        }

        // 3. Sửa bình luận (Chỉ tác giả mới được sửa)
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateComment(int id, [FromBody] UpdateCommentDto dto)
        {
            try
            {
                var comment = await _context.QuestionComments.FindAsync(id);
                if (comment == null)
                {
                    return NotFound(new { message = "Bình luận không tồn tại." });
                }

                if (comment.UserId != dto.UserId)
                {
                    return Forbid("Bạn không có quyền chỉnh sửa bình luận này.");
                }

                comment.Content = dto.Content;
                comment.IsEdited = true;
                comment.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return Ok(new { message = "Cập nhật bình luận thành công!", comment });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi sửa bình luận: " + ex.Message });
            }
        }

        // 4. Xóa bình luận (Tác giả hoặc Giáo viên/Admin được xóa)
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteComment(int id, [FromQuery] int? userId = null)
        {
            try
            {
                var comment = await _context.QuestionComments
                    .Include(c => c.Replies)
                    .FirstOrDefaultAsync(c => c.Id == id);

                if (comment == null)
                {
                    return NotFound(new { message = "Bình luận không tồn tại." });
                }

                int currentUserId = userId ?? 0;
                if (currentUserId <= 0 && Request.Headers.TryGetValue("X-User-Id", out var headerId) && int.TryParse(headerId, out int parsedHeaderId))
                {
                    currentUserId = parsedHeaderId;
                }

                var user = await _context.Users.FindAsync(currentUserId);
                if (user == null)
                {
                    return Unauthorized(new { message = "Người dùng không hợp lệ." });
                }

                bool canDelete = comment.UserId == currentUserId || user.Role == "Teacher" || user.Role == "Admin";
                if (!canDelete)
                {
                    _logger.LogWarning(
                        "Từ chối xóa bình luận {CommentId}: user {UserId} ({Role}) không phải tác giả {AuthorId}",
                        id, currentUserId, user.Role, comment.UserId);
                    return Forbid("Bạn không có quyền xóa bình luận này.");
                }

                // Nếu có replies, soft-delete để không làm gãy cây trả lời
                if (comment.Replies.Any())
                {
                    comment.IsDeleted = true;
                    comment.Content = "Bình luận này đã bị xóa.";
                    comment.AttachmentUrl = null;
                    comment.AttachmentType = null;
                }
                else
                {
                    // Xoá cứng: dọn luôn thông báo đang trỏ tới bình luận này,
                    // Notification không có FK tới Comment nên DB không tự xoá.
                    var relatedNotifications = await _context.Notifications
                        .Where(n => n.CommentId == id)
                        .ToListAsync();
                    if (relatedNotifications.Any())
                    {
                        _context.Notifications.RemoveRange(relatedNotifications);
                    }

                    _context.QuestionComments.Remove(comment);
                }

                // Ghi lại ai xóa để còn truy vết (DB chưa có cột audit)
                _logger.LogInformation(
                    "Xóa bình luận {CommentId} của user {AuthorId} ở câu hỏi {QuestionId}: người xóa {UserId} ({UserName}, {Role}), kiểu {Mode}",
                    id, comment.UserId, comment.QuestionId, currentUserId, user.FullName, user.Role,
                    comment.IsDeleted ? "soft" : "hard");

                await _context.SaveChangesAsync();
                return Ok(new { message = "Xóa bình luận thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi xóa bình luận: " + ex.Message });
            }
        }

        // 5. Thả tim / Bỏ thả tim bình luận (Toggle Reaction & Like alias)
        [HttpPost("{id}/reaction")]
        [HttpPost("{id}/like")]
        public async Task<IActionResult> ToggleReaction(int id, [FromBody] ToggleReactionDto? dto, [FromQuery] int? userId = null)
        {
            try
            {
                var comment = await _context.QuestionComments.FindAsync(id);
                if (comment == null)
                {
                    return NotFound(new { message = "Bình luận không tồn tại." });
                }

                int effectiveUserId = (dto != null && dto.UserId > 0) ? dto.UserId : (userId ?? 0);
                if (effectiveUserId == 0) return BadRequest(new { message = "Thiếu thông tin UserId." });

                var existingReaction = await _context.CommentReactions
                    .FirstOrDefaultAsync(r => r.CommentId == id && r.UserId == effectiveUserId);

                bool isLiked;
                if (existingReaction != null)
                {
                    _context.CommentReactions.Remove(existingReaction);
                    isLiked = false;
                }
                else
                {
                    _context.CommentReactions.Add(new CommentReaction
                    {
                        CommentId = id,
                        UserId = dto.UserId,
                        ReactionType = (dto?.ReactionType ?? "heart"),
                        CreatedAt = DateTime.UtcNow
                    });
                    isLiked = true;
                }

                await _context.SaveChangesAsync();

                var newCount = await _context.CommentReactions.CountAsync(r => r.CommentId == id);
                return Ok(new { isLiked, likesCount = newCount });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi reaction: " + ex.Message });
            }
        }

        // 6. Ghim / Bỏ ghim bình luận (Chỉ Giáo viên và Admin)
        [HttpPost("{id}/pin")]
        public async Task<IActionResult> TogglePin(int id, [FromBody] PinCommentDto dto)
        {
            try
            {
                var user = await _context.Users.FindAsync(dto.UserId);
                if (user == null || (user.Role != "Teacher" && user.Role != "Admin"))
                {
                    return Forbid("Chỉ Giáo viên hoặc Admin mới có quyền ghim bình luận.");
                }

                var comment = await _context.QuestionComments.FindAsync(id);
                if (comment == null)
                {
                    return NotFound(new { message = "Bình luận không tồn tại." });
                }

                comment.IsPinned = !comment.IsPinned;
                if (comment.IsPinned)
                {
                    comment.PinnedAt = DateTime.UtcNow;
                    comment.PinnedByUserId = dto.UserId;
                }
                else
                {
                    comment.PinnedAt = null;
                    comment.PinnedByUserId = null;
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = comment.IsPinned ? "Đã ghim bình luận!" : "Đã bỏ ghim bình luận!", isPinned = comment.IsPinned });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi ghim bình luận: " + ex.Message });
            }
        }
    }
}
