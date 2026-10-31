using LyraSAT.Data;
using LyraSAT.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LyraSAT.Controllers
{
    public class CreateCommentDto
    {
        public int QuestionId { get; set; }
        public int UserId { get; set; }
        public int? ParentCommentId { get; set; }
        public int? ParentId { get; set; } // Hỗ trợ frontend truyền parentId
        public string Content { get; set; } = string.Empty;
        public string? AttachmentUrl { get; set; }
        public string? AttachmentType { get; set; } // "image" | "audio"
        public int? AudioDuration { get; set; }
    }

    public class UpdateCommentDto
    {
        public int UserId { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public class ToggleReactionDto
    {
        public int UserId { get; set; }
        public string ReactionType { get; set; } = "heart";
    }

    public class PinCommentDto
    {
        public int UserId { get; set; }
    }

    public class CommentItemDto
    {
        public int Id { get; set; }
        public int QuestionId { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string UserRole { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsEdited { get; set; }
        public int? ParentCommentId { get; set; }
        public bool IsPinned { get; set; }
        public DateTime? PinnedAt { get; set; }
        public string? AttachmentUrl { get; set; }
        public string? AttachmentType { get; set; }
        public int? AudioDuration { get; set; }
        public bool IsDeleted { get; set; }
        public int LikesCount { get; set; }
        public bool IsLikedByMe { get; set; }
        public List<CommentItemDto> Replies { get; set; } = new List<CommentItemDto>();
    }

    [Route("api/[controller]")]
    [ApiController]
    public class CommentsController : ControllerBase
    {
        private readonly LyraSatDbContext _context;

        public CommentsController(LyraSatDbContext context)
        {
            _context = context;
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
                    var test = question.Test ?? await _context.Tests.FindAsync(question.TestId);
                    string testTitle = test?.Title ?? "Bài test";
                    string classCode = test?.ClassCode ?? string.Empty;

                    // 1. Nếu là phản hồi (Reply): Gửi thông báo cho người viết câu bình luận gốc
                    if (dto.ParentCommentId.HasValue)
                    {
                        var parentComment = await _context.QuestionComments.FindAsync(dto.ParentCommentId.Value);
                        if (parentComment != null && parentComment.UserId != dto.UserId)
                        {
                            var replyNotif = new Notification
                            {
                                UserId = parentComment.UserId,
                                ActorId = dto.UserId,
                                Type = "comment_reply",
                                Title = user.Role == "Teacher"
                                    ? $"👩‍🏫 Giáo viên {user.FullName} đã giải đáp câu hỏi của bạn"
                                    : $"💬 {user.FullName} đã trả lời bình luận của bạn",
                                Content = string.IsNullOrWhiteSpace(dto.Content) ? "Đã gửi một tệp đính kèm" : (dto.Content.Length > 80 ? dto.Content.Substring(0, 80) + "..." : dto.Content),
                                QuestionId = dto.QuestionId,
                                TestId = question.TestId,
                                CommentId = newComment.Id,
                                IsRead = false,
                                CreatedAt = DateTime.UtcNow
                            };
                            _context.Notifications.Add(replyNotif);
                        }
                    }

                    // 2. Gửi thông báo cho toàn bộ Giáo viên phụ trách của lớp học này (2-4 giáo viên)
                    var teacherIds = new List<int>();
                    if (!string.IsNullOrEmpty(classCode))
                    {
                        var assigned = await _context.ClassTeachers
                            .Where(ct => ct.ClassCode == classCode)
                            .Select(ct => ct.TeacherId)
                            .ToListAsync();
                        teacherIds.AddRange(assigned);

                        var targetClass = await _context.Classes.FirstOrDefaultAsync(c => c.ClassCode == classCode);
                        if (targetClass != null && targetClass.TeacherId.HasValue && !teacherIds.Contains(targetClass.TeacherId.Value))
                        {
                            teacherIds.Add(targetClass.TeacherId.Value);
                        }
                    }

                    // Fallback: nếu lớp chưa cấu hình giáo viên, lấy tất cả giáo viên trong hệ thống
                    if (!teacherIds.Any())
                    {
                        var allTeachers = await _context.Users
                            .Where(u => u.Role == "Teacher")
                            .Select(u => u.Id)
                            .ToListAsync();
                        teacherIds.AddRange(allTeachers);
                    }

                    // Gửi thông báo chuông cho tất cả giáo viên (trừ chính người gửi nếu là giáo viên)
                    foreach (var tId in teacherIds.Distinct())
                    {
                        if (tId != dto.UserId)
                        {
                            var teacherNotif = new Notification
                            {
                                UserId = tId,
                                ActorId = dto.UserId,
                                Type = "question_comment",
                                Title = dto.ParentCommentId.HasValue
                                    ? $"💬 {user.FullName} đã phản hồi thảo luận đề '{testTitle}'"
                                    : $"💬 {user.FullName} có câu hỏi mới về đề '{testTitle}'",
                                Content = string.IsNullOrWhiteSpace(dto.Content) ? "Đã gửi câu hỏi với tệp đính kèm" : (dto.Content.Length > 80 ? dto.Content.Substring(0, 80) + "..." : dto.Content),
                                QuestionId = dto.QuestionId,
                                TestId = question.TestId,
                                CommentId = newComment.Id,
                                IsRead = false,
                                CreatedAt = DateTime.UtcNow
                            };
                            _context.Notifications.Add(teacherNotif);
                        }
                    }

                    await _context.SaveChangesAsync();
                }
                catch (Exception notifEx)
                {
                    // Lỗi thông báo không chặn luồng comment chính
                    Console.WriteLine("Lỗi gửi thông báo comment: " + notifEx.Message);
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
                    _context.QuestionComments.Remove(comment);
                }

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
