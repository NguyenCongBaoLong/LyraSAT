using LyraSAT.Data;
using LyraSAT.DTOs;
using LyraSAT.Models;
using Microsoft.EntityFrameworkCore;

namespace LyraSAT.Services
{
    /// <summary>
    /// Tập trung toàn bộ logic tạo thông báo (chuông) cho giáo viên, học sinh và quản trị viên.
    /// Mọi lỗi khi tạo thông báo đều được ghi log thật (ILogger) chứ không nuốt im lặng.
    /// </summary>
    public interface INotificationService
    {
        Task<List<int>> GetClassTeacherIdsAsync(string classCode);
        Task NotifyTestSubmittedAsync(
            int studentId,
            string? studentName,
            string classCode,
            int testId,
            string testTitle,
            int correctCount,
            int totalQuestions,
            double scorePercent,
            int timeSpentSeconds);
        Task NotifyCommentCreatedAsync(
            Question question,
            User author,
            CreateCommentDto dto,
            int newCommentId);
        Task NotifyTestAssignedAsync(Test test, int? actorId = null);
    }

    public class NotificationService : INotificationService
    {
        private readonly LyraSatDbContext _context;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(LyraSatDbContext context, ILogger<NotificationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>Toàn bộ tài khoản quản trị - luôn nhận thông báo hoạt động của hệ thống.</summary>
        private Task<List<int>> GetAdminIdsAsync()
        {
            return _context.Users
                .Where(u => u.Role == "Admin")
                .Select(u => u.Id)
                .ToListAsync();
        }

        /// <summary>
        /// Giáo viên phụ trách của một lớp (2-4 giáo viên), gồm cả giáo viên chính trong bảng Classes.
        /// Compare mã lớp không phân biệt hoa/thường để tránh lệch "SF2609" / "sf2609".
        /// </summary>
        public async Task<List<int>> GetClassTeacherIdsAsync(string classCode)
        {
            var code = ClassCodeHelper.Normalize(classCode);
            if (code.Length == 0)
            {
                return new List<int>();
            }

            var lower = code.ToLower();

            var teacherIds = await _context.ClassTeachers
                .AsNoTracking()
                .Where(ct => ct.ClassCode.ToLower() == lower)
                .Select(ct => ct.TeacherId)
                .ToListAsync();

            var leadTeacherIds = await _context.Classes
                .AsNoTracking()
                .Where(c => c.ClassCode.ToLower() == lower && c.TeacherId.HasValue)
                .Select(c => c.TeacherId!.Value)
                .ToListAsync();

            teacherIds.AddRange(leadTeacherIds);

            return teacherIds.Distinct().ToList();
        }

        /// <summary>Học sinh thuộc lớp: theo bảng StudentClasses và theo cột Users.ClassCode (dữ liệu cũ).</summary>
        private async Task<List<int>> GetClassStudentIdsAsync(string classCode)
        {
            var code = ClassCodeHelper.Normalize(classCode);
            if (code.Length == 0) return new List<int>();

            var lower = code.ToLower();

            var byAssignment = await _context.StudentClasses
                .AsNoTracking()
                .Where(sc => sc.Class != null && sc.Class.ClassCode.ToLower() == lower && sc.User != null && sc.User.Role == "Student")
                .Select(sc => sc.UserId)
                .ToListAsync();

            var byProfile = await _context.Users
                .AsNoTracking()
                .Where(u => u.Role == "Student" && u.ClassCode != null && u.ClassCode.ToLower() == lower)
                .Select(u => u.Id)
                .ToListAsync();

            return byAssignment.Concat(byProfile).Distinct().ToList();
        }

        /// <summary>
        /// Nộp bài: báo cho toàn bộ giáo viên phụ trách lớp + quản trị viên (trừ chính người nộp).
        /// </summary>
        public async Task NotifyTestSubmittedAsync(
            int studentId,
            string? studentName,
            string classCode,
            int testId,
            string testTitle,
            int correctCount,
            int totalQuestions,
            double scorePercent,
            int timeSpentSeconds)
        {
            var teacherIds = await GetClassTeacherIdsAsync(classCode);

            if (teacherIds.Count == 0)
            {
                // Lớp chưa cấu hình giáo viên: giữ fallback cũ (tất cả giáo viên) nhưng ghi log để còn truy vết.
                _logger.LogWarning(
                    "Không tìm thấy giáo viên cho lớp {ClassCode} (test {TestId}). Fallback: gửi cho tất cả giáo viên.",
                    classCode, testId);

                teacherIds = await _context.Users
                    .Where(u => u.Role == "Teacher")
                    .Select(u => u.Id)
                    .ToListAsync();
            }

            var adminIds = await GetAdminIdsAsync();
            var recipients = teacherIds.Concat(adminIds).Distinct().Where(id => id != studentId).ToList();

            string displayName = studentName ?? "Học sinh";

            foreach (var tId in recipients)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = tId,
                    ActorId = studentId,
                    Type = "test_submission",
                    Title = $"📝 {displayName} đã nộp bài test '{testTitle}'",
                    Content = $"Kết quả: {correctCount}/{totalQuestions} câu đúng ({scorePercent}%). Thời gian làm: {timeSpentSeconds / 60} phút.",
                    TestId = testId,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Bình luận mới: báo người được trả lời (nếu là reply), toàn bộ giáo viên phụ trách lớp và quản trị viên.
        /// </summary>
        public async Task NotifyCommentCreatedAsync(
            Question question,
            User author,
            CreateCommentDto dto,
            int newCommentId)
        {
            var test = question.Test ?? await _context.Tests.FindAsync(question.TestId);
            string testTitle = test?.Title ?? "Bài test";
            string classCode = test?.ClassCode ?? string.Empty;

            string shortContent = string.IsNullOrWhiteSpace(dto.Content)
                ? "Đã gửi một tệp đính kèm"
                : (dto.Content.Length > 80 ? dto.Content.Substring(0, 80) + "..." : dto.Content);

            // 1. Nếu là phản hồi: gửi thông báo cho người viết bình luận gốc
            if (dto.ParentCommentId.HasValue)
            {
                var parentComment = await _context.QuestionComments.FindAsync(dto.ParentCommentId.Value);
                if (parentComment != null && parentComment.UserId != dto.UserId)
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = parentComment.UserId,
                        ActorId = dto.UserId,
                        Type = "comment_reply",
                        Title = author.Role == "Teacher"
                            ? $"👩‍🏫 Giáo viên {author.FullName} đã giải đáp câu hỏi của bạn"
                            : $"💬 {author.FullName} đã trả lời bình luận của bạn",
                        Content = shortContent,
                        QuestionId = dto.QuestionId,
                        TestId = question.TestId,
                        CommentId = newCommentId,
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // 2. Giáo viên phụ trách lớp + quản trị viên (trừ chính người gửi)
            var teacherIds = await GetClassTeacherIdsAsync(classCode);
            var adminIds = await GetAdminIdsAsync();
            var recipients = teacherIds.Concat(adminIds).Distinct().Where(id => id != dto.UserId).ToList();

            foreach (var tId in recipients)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = tId,
                    ActorId = dto.UserId,
                    Type = "question_comment",
                    Title = dto.ParentCommentId.HasValue
                        ? $"💬 {author.FullName} đã phản hồi thảo luận đề '{testTitle}'"
                        : $"💬 {author.FullName} có câu hỏi mới về đề '{testTitle}'",
                    Content = shortContent,
                    QuestionId = dto.QuestionId,
                    TestId = question.TestId,
                    CommentId = newCommentId,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Giao đề mới: báo cho toàn bộ học sinh của lớp (kể cả học sinh chưa mở app).
        /// </summary>
        public async Task NotifyTestAssignedAsync(Test test, int? actorId = null)
        {
            var classCode = ClassCodeHelper.Normalize(test.ClassCode);
            if (classCode.Length == 0)
            {
                _logger.LogWarning("Đề {TestId} không có mã lớp, bỏ qua thông báo giao đề.", test.Id);
                return;
            }

            var studentIds = await GetClassStudentIdsAsync(classCode);
            if (studentIds.Count == 0)
            {
                _logger.LogInformation("Lớp {ClassCode} chưa có học sinh, không có ai để báo đề {TestId}.", classCode, test.Id);
                return;
            }

            string deadlineText = test.Deadline.HasValue
                ? $" Hạn nộp: {test.Deadline.Value:dd/MM/yyyy HH:mm} (UTC)."
                : string.Empty;

            foreach (var studentId in studentIds.Where(id => id != actorId))
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = studentId,
                    ActorId = actorId,
                    Type = "test_assigned",
                    Title = $"📚 Đề mới: {test.Title}",
                    Content = $"Lớp {classCode} vừa được giao đề '{test.Title}'.{deadlineText}",
                    TestId = test.Id,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
        }
    }
}
