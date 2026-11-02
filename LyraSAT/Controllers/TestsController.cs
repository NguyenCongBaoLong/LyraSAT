using LyraSAT.Data;
using LyraSAT.Models;
using LyraSAT.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.XWPF.UserModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LyraSAT.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TestsController : ControllerBase
    {
        private readonly LyraSatDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly ILogger<TestsController> _logger;

        public TestsController(
            LyraSatDbContext context,
            INotificationService notificationService,
            ILogger<TestsController> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _logger = logger;
        }

        // Lấy danh sách bài test theo mã lớp (Mục Mission)
        [HttpGet("class/{classCode}")]
        public async Task<IActionResult> GetTestsByClass(
            string classCode,
            [FromQuery] string? status,
            [FromQuery] int? studentId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 8)
        {
            if (status?.Equals("expired", StringComparison.OrdinalIgnoreCase) == true &&
                (!studentId.HasValue || studentId.Value <= 0))
            {
                return BadRequest(new { message = "Mã học sinh không hợp lệ." });
            }

            var now = DateTime.UtcNow;
            var normalizedClassCode = ClassCodeHelper.Normalize(classCode).ToLower();

            var tests = _context.Tests
                .AsNoTracking()
                .Where(t => t.ClassCode != null && t.ClassCode.ToLower() == normalizedClassCode);

            tests = status?.ToLowerInvariant() switch
            {
                "urgent" => tests.Where(t => t.Deadline > now && t.Deadline <= now.AddHours(24)),
                "expired" => tests.Where(t => t.Deadline <= now),
                "active" => tests.Where(t => t.Deadline == null || t.Deadline > now.AddHours(24)),
                _ => tests
            };

            if (status?.Equals("expired", StringComparison.OrdinalIgnoreCase) == true && studentId.HasValue)
            {
                tests = tests.Where(t => !_context.Submissions.Any(s =>
                    s.TestId == t.Id &&
                    s.StudentId == studentId.Value &&
                    s.Status == "Completed"));
            }

            var result = await tests
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.ClassCode,
                    t.Duration,
                    t.Deadline,
                    t.TestType,
                    t.CreatedAt
                })
                .ToPagedResultAsync(page, pageSize);
            return Ok(result);
        }

        // Lấy chi tiết câu hỏi của bài test (Khi học sinh bấm vào làm bài)
        [HttpGet("{id}/questions")]
        public async Task<IActionResult> GetTestQuestions(int id)
        {
            var test = await _context.Tests
                .Include(t => t.Questions)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (test == null) return NotFound(new { message = "Không tìm thấy bài test!" });

            // Ẩn đáp án đúng và video giải thích khi đang làm bài
            var secureQuestions = test.Questions
                .OrderBy(q => q.Id)
                .Select(q => new
            {
                q.Id,
                q.QuestionText,
                q.OptionA,
                q.OptionB,
                q.OptionC,
                q.OptionD
            });

            return Ok(new { test.Title, test.Duration, Questions = secureQuestions });
        }

        // Giáo viên tạo bài test mới (Thủ công)
        [HttpPost("create")]
        public async Task<IActionResult> CreateTest([FromBody] Test model)
        {
            // Nếu không nhập hoặc nhập <= 0 thì gán là null (Vô hạn số lần)
            if (model.MaxAttempts.HasValue && model.MaxAttempts <= 0)
            {
                model.MaxAttempts = null;
            }

            model.ClassCode = ClassCodeHelper.Normalize(model.ClassCode);
            if (model.ClassCode.Length == 0)
            {
                return BadRequest(new { message = "Vui lòng chọn lớp học cho đề thi." });
            }

            // Lưu đúng mã lớp như trong bảng Classes để không sinh ra biến thể hoa/thường
            var targetClass = await ClassCodeHelper.FindClassAsync(_context, model.ClassCode);
            if (targetClass == null)
            {
                return BadRequest(new { message = $"Mã lớp '{model.ClassCode}' không tồn tại. Vui lòng kiểm tra lại." });
            }
            model.ClassCode = targetClass.ClassCode;

            _context.Tests.Add(model);
            await _context.SaveChangesAsync();

            await TryNotifyTestAssignedAsync(model, null);

            return Ok(new { message = "Tạo bài tập thành công!", testId = model.Id });
        }

        // Giáo viên upload bài test qua file Word (.docx)
        [HttpPost("upload-test")]
        public async Task<IActionResult> UploadTest(
            [FromForm] string title,
            [FromForm] string classCode,
            [FromForm] int duration,
            [FromForm] string testType,
            [FromForm] DateTimeOffset? deadline,
            [FromForm] int? maxAttempts,
            [FromForm] string? videoExplanationUrl,
            IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "Vui lòng chọn file Word (.docx) tải lên!" });
            }

            if (!file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Hệ thống chỉ hỗ trợ định dạng file Word (.docx)!" });
            }

            try
            {
                int? finalMaxAttempts = (maxAttempts.HasValue && maxAttempts.Value > 0) ? maxAttempts.Value : null;

                // 0. Chuẩn hoá và kiểm tra mã lớp: chặn tạo đề cho lớp không tồn tại (nguồn gốc lỗi "lớp ma")
                string normalizedClassCode = ClassCodeHelper.Normalize(classCode);
                if (normalizedClassCode.Length == 0)
                {
                    return BadRequest(new { message = "Vui lòng chọn lớp học cho đề thi." });
                }

                // Dùng đúng chuỗi mã lớp trong bảng Classes (tránh lệch hoa/thường giữa các bảng)
                var targetClass = await ClassCodeHelper.FindClassAsync(_context, normalizedClassCode);
                if (targetClass == null)
                {
                    return BadRequest(new { message = $"Mã lớp '{normalizedClassCode}' không tồn tại. Vui lòng chọn lại lớp." });
                }
                normalizedClassCode = targetClass.ClassCode;
                // 1. Tạo bài test mới trong cơ sở dữ liệu
                var newTest = new Test
                {
                    Title = title,
                    ClassCode = normalizedClassCode,
                    Duration = duration,
                    TestType = testType,
                    Deadline = deadline?.UtcDateTime,
                    MaxAttempts = finalMaxAttempts,
                    CreatedAt = DateTime.UtcNow,
                    VideoExplanationUrl = videoExplanationUrl
                };

                _context.Tests.Add(newTest);
                await _context.SaveChangesAsync(); // Lưu để lấy TestId tự tăng

                // 2. Đọc file Word (.docx) bằng thư viện NPOI, hỗ trợ trích xuất cả Paragraph và Table
                using var stream = file.OpenReadStream();
                var document = new XWPFDocument(stream);

                var extractedLines = SatWordTestParser.ExtractDocumentLines(document);

                // 3. Bóc tách câu hỏi SAT
                var questions = SatWordTestParser.Parse(extractedLines, newTest.Id);

                // KIỂM TRA: Nếu không có câu hỏi hợp lệ -> Xóa bài test vừa tạo và báo lỗi
                if (questions.Count == 0)
                {
                    _context.Tests.Remove(newTest);
                    await _context.SaveChangesAsync();
                    return BadRequest(new { message = "File Word không đúng cấu trúc định dạng câu hỏi SAT (Thiếu các từ khóa Text:, Question:, hoặc các đáp án A, B, C, D)!" });
                }

                _context.Questions.AddRange(questions);
                await _context.SaveChangesAsync();

                // Báo cho học sinh của lớp biết có đề mới
                await TryNotifyTestAssignedAsync(newTest, null);

                return Ok(new { message = $"Tải lên và xử lý thành công {questions.Count} câu hỏi từ file Word!", testId = newTest.Id });
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null ? " -> " + ex.InnerException.Message : "";
                return StatusCode(500, new { message = "Lỗi khi xử lý file Word: " + ex.Message + innerMsg });
            }
        }

        /// <summary>Gửi thông báo giao đề; lỗi thông báo không được làm hỏng luồng tạo đề.</summary>
        private async Task TryNotifyTestAssignedAsync(Test test, int? actorId)
        {
            try
            {
                await _notificationService.NotifyTestAssignedAsync(test, actorId);
            }
            catch (Exception notifEx)
            {
                _logger.LogError(notifEx, "Lỗi gửi thông báo giao đề test {TestId}", test.Id);
            }
        }

        [HttpGet("class/{classCode}/results")]
        public async Task<IActionResult> GetTestResultsByClass(
            string classCode,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            var normalizedClassCode = ClassCodeHelper.Normalize(classCode).ToLower();

            var results = await _context.Tests
                .AsNoTracking()
                .Where(t => t.ClassCode != null && t.ClassCode.ToLower() == normalizedClassCode)
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .ToPagedResultAsync(page, pageSize);

            return Ok(results);
        }

        [HttpGet("class/{classCode}/students")]
        public async Task<IActionResult> GetStudentsByClass(
            string classCode,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            var normalizedClassCode = ClassCodeHelper.Normalize(classCode);
            var lowerClassCode = normalizedClassCode.ToLower();

            var students = await _context.StudentClasses
                .AsNoTracking()
                .Where(sc => sc.Class != null && sc.Class.ClassCode.ToLower() == lowerClassCode && sc.User != null && sc.User.Role == "Student")
                .OrderBy(sc => sc.User!.FullName)
                .ThenBy(sc => sc.UserId)
                .Select(sc => new
                {
                    sc.User!.Id,
                    sc.User.FullName,
                    ClassCode = normalizedClassCode,
                    TuitionPaid = sc.User.TuitionStatus ?? false
                })
                .ToPagedResultAsync(page, pageSize);
            return Ok(students);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTestDetail(int id)
        {
            var test = await _context.Tests
                .Where(t => t.Id == id)
                .Select(t => new
                {
                    id = t.Id,
                    title = t.Title ?? "Bài test không tên",
                    duration = t.Duration,
                    videoExplanationUrl = t.VideoExplanationUrl ?? "",
                    questions = t.Questions
                        .OrderBy(q => q.Id)
                        .Select(q => new
                    {
                        id = q.Id,
                        questionText = q.QuestionText ?? "",
                        passageText = q.PassageText ?? q.Explanation ?? "",
                        optionA = q.OptionA ?? "",
                        optionB = q.OptionB ?? "",
                        optionC = q.OptionC ?? "",
                        optionD = q.OptionD ?? ""
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (test == null) return NotFound(new { message = "Không tìm thấy bài test" });
            return Ok(test);
        }

        [HttpGet("{testId}/submissions")]
        public async Task<IActionResult> GetTestSubmissions(
            int testId,
            [FromQuery] int? studentId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 8)
        {
            try
            {
                var query = _context.Submissions
                    .Where(s => s.TestId == testId);

                // Dùng cho deep-link từ thông báo: mở đúng bài nộp của một học sinh.
                if (studentId.HasValue)
                {
                    query = query.Where(s => s.StudentId == studentId.Value);
                }

                var submissions = await query
                    .AsNoTracking()
                    .OrderByDescending(s => s.SubmittedAt)
                    .ThenByDescending(s => s.Id)
                    .Include(s => s.Student)
                    .Select(s => new
                    {
                        id = s.Id,
                        studentId = s.StudentId,
                        studentName = s.Student != null ? (s.Student.FullName ?? s.Student.Email) : "Học sinh",
                        submittedAt = s.SubmittedAt,
                        timeSpent = s.TimeSpent,
                        correctCount = s.CorrectCount,
                        totalQuestions = s.TotalQuestions,
                        score = s.Score
                    })
                    .ToPagedResultAsync(page, pageSize);

                return Ok(submissions);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // Kiểm tra số lần làm bài (Hỗ trợ vô hạn nếu MaxAttempts = null)
        [HttpGet("{testId}/check-attempts/{studentId}")]
        public async Task<IActionResult> CheckStudentAttempts(int testId, string studentId)
        {
            try
            {
                if (!int.TryParse(studentId, out int parsedStudentId))
                {
                    return BadRequest(new { message = "Mã học sinh không hợp lệ." });
                }

                var test = await _context.Tests.FindAsync(testId);
                if (test == null)
                    return NotFound(new { message = "Không tìm thấy bài test." });

                var submissionCount = await _context.Submissions
                    .CountAsync(s => s.TestId == testId && s.StudentId == parsedStudentId);

                // NẾU MaxAttempts có giá trị (khác null) và số lần làm >= MaxAttempts mới chặn
                if (test.MaxAttempts.HasValue && submissionCount >= test.MaxAttempts.Value)
                {
                    return Ok(new
                    {
                        allowed = false,
                        submissionCount = submissionCount,
                        maxAttempts = test.MaxAttempts.Value,
                        videoExplanationUrl = test.VideoExplanationUrl,
                        testTitle = test.Title,
                        message = $"Bạn đã hết số lần làm bài cho phép ({submissionCount}/{test.MaxAttempts.Value})."
                    });
                }

                // Nếu MaxAttempts là null -> Làm bao nhiêu lần cũng được (Vô hạn)
                return Ok(new
                {
                    allowed = true,
                    submissionCount = submissionCount,
                    maxAttempts = test.MaxAttempts, // Có thể là null hoặc số cụ thể
                    remainingAttempts = test.MaxAttempts.HasValue ? (int?)(test.MaxAttempts.Value - submissionCount) : null
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // Xóa bài test (Dành cho Giáo viên & Quản trị viên)
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTest(int id, [FromQuery] int? userId = null)
        {
            try
            {
                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                    return NotFound(new { message = "Không tìm thấy bài test." });

                int currentUserId = userId ?? 0;
                if (currentUserId <= 0 && Request.Headers.TryGetValue("X-User-Id", out var hId) && int.TryParse(hId, out int parsedHId))
                {
                    currentUserId = parsedHId;
                }

                if (currentUserId > 0)
                {
                    var user = await _context.Users.FindAsync(currentUserId);
                    if (user != null && user.Role != "Teacher" && user.Role != "Admin")
                    {
                        return Forbid("Chỉ Giáo viên hoặc Quản trị viên mới được phép xóa bài test.");
                    }
                }

                var questionIds = await _context.Questions.Where(q => q.TestId == id).Select(q => q.Id).ToListAsync();
                var submissions = await _context.Submissions.Where(s => s.TestId == id).ToListAsync();
                var submissionIds = submissions.Select(s => s.Id).ToList();

                var subDetails = await _context.SubmissionDetails.Where(sd => submissionIds.Contains(sd.SubmissionId) || questionIds.Contains(sd.QuestionId)).ToListAsync();
                if (subDetails.Any())
                {
                    _context.SubmissionDetails.RemoveRange(subDetails);
                }

                var comments = await _context.QuestionComments.Where(c => questionIds.Contains(c.QuestionId)).ToListAsync();
                if (comments.Any())
                {
                    var commentIds = comments.Select(c => c.Id).ToList();
                    var reactions = await _context.CommentReactions.Where(r => commentIds.Contains(r.CommentId)).ToListAsync();
                    if (reactions.Any())
                    {
                        _context.CommentReactions.RemoveRange(reactions);
                    }
                    _context.QuestionComments.RemoveRange(comments);
                }

                if (submissions.Any())
                {
                    _context.Submissions.RemoveRange(submissions);
                }

                var questions = await _context.Questions.Where(q => q.TestId == id).ToListAsync();
                if (questions.Any())
                {
                    _context.Questions.RemoveRange(questions);
                }

                // Dọn thông báo trỏ tới đề/câu hỏi sắp xóa (tránh chuông còn thông báo "mồ côi")
                var relatedNotifications = await _context.Notifications
                    .Where(n => n.TestId == id || (n.QuestionId.HasValue && questionIds.Contains(n.QuestionId.Value)))
                    .ToListAsync();
                if (relatedNotifications.Any())
                {
                    _context.Notifications.RemoveRange(relatedNotifications);
                }

                _context.Tests.Remove(test);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Đã xóa bài test thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi xóa bài test: " + ex.Message });
            }
        }

        // Gỡ học sinh khỏi lớp (Dành cho Giáo viên khi add nhầm)
        [HttpPost("class/{classCode}/remove-student/{studentId}")]
        [HttpDelete("class/{classCode}/students/{studentId}")]
        public async Task<IActionResult> RemoveStudentFromClass(string classCode, int studentId)
        {
            try
            {
                var normalizedClassCode = ClassCodeHelper.Normalize(classCode);
                var lowerClassCode = normalizedClassCode.ToLower();

                var assignment = await _context.StudentClasses
                    .Include(sc => sc.Class)
                    .Include(sc => sc.User)
                    .FirstOrDefaultAsync(sc => sc.UserId == studentId && sc.Class != null && sc.Class.ClassCode.ToLower() == lowerClassCode);
                if (assignment == null)
                {
                    return NotFound(new { message = "Không tìm thấy học sinh trong lớp này." });
                }

                _context.StudentClasses.Remove(assignment);
                if (assignment.User != null && ClassCodeHelper.SameCode(assignment.User.ClassCode, normalizedClassCode))
                {
                    var fallbackClassCode = await _context.StudentClasses
                        .Where(sc => sc.UserId == studentId && sc.ClassId != assignment.ClassId)
                        .OrderBy(sc => sc.AssignedAt)
                        .Select(sc => sc.Class!.ClassCode)
                        .FirstOrDefaultAsync();
                    assignment.User.ClassCode = fallbackClassCode;
                }
                await _context.SaveChangesAsync();

                return Ok(new { message = $"Đã gỡ học viên {assignment.User?.FullName ?? "học viên"} ra khỏi lớp {classCode} thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi gỡ học sinh khỏi lớp: " + ex.Message });
            }
        }
    }
}
