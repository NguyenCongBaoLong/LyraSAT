using LyraSAT.Data;
using LyraSAT.DTOs;
using LyraSAT.Models;
using LyraSAT.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.XWPF.UserModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
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
        private readonly IWebHostEnvironment _env;
        private readonly ISupabaseStorageService _storage;

        public TestsController(
            LyraSatDbContext context,
            INotificationService notificationService,
            ILogger<TestsController> logger,
            IWebHostEnvironment env,
            ISupabaseStorageService storage)
        {
            _context = context;
            _notificationService = notificationService;
            _logger = logger;
            _env = env;
            _storage = storage;
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
            if ((status?.Equals("expired", StringComparison.OrdinalIgnoreCase) == true ||
                 status?.Equals("completed", StringComparison.OrdinalIgnoreCase) == true) &&
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
                "completed" => tests.Where(t => t.MaxAttempts.HasValue &&
                    _context.Submissions.Count(s =>
                        s.TestId == t.Id &&
                        s.StudentId == studentId!.Value &&
                        s.Status == "Completed") >= t.MaxAttempts.Value),
                _ => tests
            };

            if ((status?.Equals("urgent", StringComparison.OrdinalIgnoreCase) == true ||
                 status?.Equals("active", StringComparison.OrdinalIgnoreCase) == true) &&
                studentId.HasValue)
            {
                tests = tests.Where(t => !t.MaxAttempts.HasValue ||
                    _context.Submissions.Count(s =>
                        s.TestId == t.Id &&
                        s.StudentId == studentId.Value &&
                        s.Status == "Completed") < t.MaxAttempts.Value);
            }

            if (status?.Equals("expired", StringComparison.OrdinalIgnoreCase) == true && studentId.HasValue)
            {
                tests = tests.Where(t => !_context.Submissions.Any(s =>
                    s.TestId == t.Id &&
                    s.StudentId == studentId.Value &&
                    s.Status == "Completed"));
            }

            var orderedTests = studentId.HasValue
                ? tests
                    .OrderBy(t => t.Deadline == null)
                    .ThenBy(t => t.Deadline)
                    .ThenByDescending(t => t.CreatedAt)
                    .ThenByDescending(t => t.Id)
                : tests
                    .OrderByDescending(t => t.CreatedAt)
                    .ThenByDescending(t => t.Id);

            var result = await orderedTests
                .Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.ClassCode,
                    t.Duration,
                    t.Deadline,
                    t.TestType,
                    t.CreatedAt,
                    // Giáo viên cần thấy đề nào đã có video chữa bài, để bổ sung sau khi up đề.
                    t.VideoExplanationUrl,
                    VideoCount = t.Videos.Count()
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
                .ThenInclude(q => q.Assets)
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
                q.OptionD,
                Assets = q.Assets.OrderBy(asset => asset.SortOrder).Select(asset => new
                {
                    asset.Id,
                    asset.AssetType,
                    asset.FileUrl,
                    asset.FileName,
                    asset.MimeType,
                    asset.AltText,
                    asset.Placement,
                    asset.SortOrder
                })
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

                // KIỂM TRA: Không lưu đề nếu dữ liệu đáp án bị sai (thiếu đáp án, trùng nhau,
                // còn dính nhãn chữ cái, hoặc không đọc được đáp án đúng). Thà báo để giáo viên
                // sửa file còn hơn để học sinh làm đề có đáp án sai.
                var optionIssues = SatWordTestParser.Validate(questions);
                if (optionIssues.Count > 0)
                {
                    _context.Tests.Remove(newTest);
                    await _context.SaveChangesAsync();

                    var preview = string.Join(" ", optionIssues.Take(5));
                    var more = optionIssues.Count > 5 ? $" (còn {optionIssues.Count - 5} lỗi khác)" : "";
                    return BadRequest(new
                    {
                        message = $"File Word có {optionIssues.Count} lỗi ở phần đáp án nên chưa được lưu: {preview}{more}",
                        issues = optionIssues
                    });
                }

                _context.Questions.AddRange(questions);
                await _context.SaveChangesAsync();
                await ExtractAndPersistQuestionAssetsAsync(questions, newTest.Id);

                // Nếu giáo viên dán link video ngay khi giao đề, đồng thời tạo luôn
                // một dòng trong TestVideos để danh sách nhiều video thấy được ngay.
                var initialVideo = videoExplanationUrl?.Trim();
                if (!string.IsNullOrWhiteSpace(initialVideo))
                {
                    _context.TestVideos.Add(new LyraSAT.Models.TestVideo
                    {
                        TestId = newTest.Id,
                        Title = "Video chữa bài",
                        Url = initialVideo,
                        SortOrder = 0,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                }

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

        // Giới hạn dung lượng file video chữa bài: 150MB.
        private const long MaxVideoBytes = 150L * 1024 * 1024;

        private static readonly string[] AllowedVideoExts =
            { ".mp4", ".m4v", ".webm", ".mov", ".ogv" };

        /// <summary>
        /// Gắn / đổi / gỡ link video chữa bài cho một đề ĐÃ giao.
        /// Giáo viên không phải tạo lại đề chỉ vì quên chèn video.
        /// </summary>
        [HttpPut("{id}/video")]
        public async Task<IActionResult> UpdateVideoExplanation(int id, [FromBody] UpdateVideoExplanationDto dto)
        {
            var test = await _context.Tests.FirstOrDefaultAsync(t => t.Id == id);
            if (test == null)
            {
                return NotFound(new { message = "Không tìm thấy bài test!" });
            }

            var url = dto?.VideoExplanationUrl?.Trim() ?? string.Empty;

            // Chỉ nhận link http(s) hoặc file đã upload nội bộ; chặn javascript:, data: ...
            if (url.Length > 0 &&
                !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Link video không hợp lệ. Chỉ nhận link http(s) hoặc file đã tải lên." });
            }

            test.VideoExplanationUrl = url.Length == 0 ? null : url;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = url.Length == 0 ? "Đã gỡ video chữa bài." : "Đã lưu video chữa bài.",
                testId = test.Id,
                videoExplanationUrl = test.VideoExplanationUrl ?? ""
            });
        }

        /// <summary>
        /// Tải file video chữa bài lên cho một đề ĐÃ giao, rồi gán luôn vào đề.
        /// Lưu ý: file nằm trên ổ đĩa của server, sẽ mất nếu server deploy lại.
        /// </summary>
        [HttpPost("{id}/video-file")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(MaxVideoBytes + 5 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxVideoBytes + 5 * 1024 * 1024)]
        public async Task<IActionResult> UploadVideoExplanation(int id, IFormFile file)
        {
            var test = await _context.Tests.FirstOrDefaultAsync(t => t.Id == id);
            if (test == null)
            {
                return NotFound(new { message = "Không tìm thấy bài test!" });
            }

            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "Không tìm thấy file video tải lên." });
            }

            if (file.Length > MaxVideoBytes)
            {
                return BadRequest(new { message = "File video vượt quá giới hạn 150MB." });
            }

            var ext = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
            if (!AllowedVideoExts.Contains(ext))
            {
                return BadRequest(new { message = "Định dạng video không hỗ trợ. Chỉ nhận mp4, m4v, webm, mov, ogv." });
            }

            try
            {
                var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var uploadDir = Path.Combine(webRoot, "uploads", "tests");
                Directory.CreateDirectory(uploadDir);

                var fileName = $"{Guid.NewGuid():N}{ext}";
                var filePath = Path.Combine(uploadDir, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var url = $"/uploads/tests/{fileName}";
                test.VideoExplanationUrl = url;
                await _context.SaveChangesAsync();

                return Ok(new { message = "Đã tải video lên và gắn vào bài test.", videoExplanationUrl = url });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi lưu video chữa bài cho test {TestId}", id);
                return StatusCode(500, new { message = "Lỗi khi lưu file video: " + ex.Message });
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
                    videos = t.Videos
                        .OrderBy(v => v.SortOrder)
                        .ThenBy(v => v.Id)
                        .Select(v => new
                        {
                            id = v.Id,
                            title = v.Title ?? "",
                            url = v.Url
                        }).ToList(),
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
                        optionD = q.OptionD ?? "",
                        assets = q.Assets
                            .OrderBy(asset => asset.Placement)
                            .ThenBy(asset => asset.SortOrder)
                            .ThenBy(asset => asset.Id)
                            .Select(asset => new
                            {
                                id = asset.Id,
                                assetType = asset.AssetType,
                                fileUrl = asset.FileUrl,
                                fileName = asset.FileName,
                                mimeType = asset.MimeType,
                                altText = asset.AltText ?? "",
                                placement = asset.Placement,
                                sortOrder = asset.SortOrder
                            }).ToList()
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
                        videos = await _context.TestVideos
                            .AsNoTracking()
                            .Where(v => v.TestId == testId)
                            .OrderBy(v => v.SortOrder)
                            .ThenBy(v => v.Id)
                            .Select(v => new LyraSAT.DTOs.TestVideoItemDto
                            {
                                Id = v.Id,
                                Title = v.Title ?? "",
                                Url = v.Url
                            })
                            .ToListAsync(),
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

        private async Task ExtractAndPersistQuestionAssetsAsync(IEnumerable<Question> questions, int testId)
        {
            var dataUriPattern = new Regex(
                "data:(?<mime>image\\/(?:png|jpeg|jpg|gif|webp));base64,(?<data>[A-Za-z0-9+/=]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var assetIndex = 0;

            foreach (var question in questions)
            {
                var fields = new[]
                {
                    (Name: "PassageText", Value: question.PassageText, Placement: "Passage"),
                    (Name: "QuestionText", Value: question.QuestionText, Placement: "Question"),
                    (Name: "OptionA", Value: question.OptionA, Placement: "OptionA"),
                    (Name: "OptionB", Value: question.OptionB, Placement: "OptionB"),
                    (Name: "OptionC", Value: question.OptionC, Placement: "OptionC"),
                    (Name: "OptionD", Value: question.OptionD, Placement: "OptionD")
                };

                foreach (var field in fields)
                {
                    if (string.IsNullOrEmpty(field.Value)) continue;

                    var fieldAssetIndex = 0;
                    var matches = dataUriPattern.Matches(field.Value);
                    var replacedBuilder = new StringBuilder();
                    var previousIndex = 0;

                    foreach (Match match in matches)
                    {
                        var mimeType = match.Groups["mime"].Value.ToLowerInvariant();
                        var extension = mimeType is "image/jpeg" or "image/jpg" ? "jpg" : mimeType[6..];
                        var fileName = $"question-{question.Id}-{field.Placement.ToLowerInvariant()}-{fieldAssetIndex++}.{extension}";
                        var bytes = Convert.FromBase64String(match.Groups["data"].Value);
                        var objectPath = $"tests/{testId}/{fileName}";
                        var fileUrl = await _storage.UploadAsync(bytes, objectPath, mimeType);

                        replacedBuilder.Append(field.Value, previousIndex, match.Index - previousIndex);
                        replacedBuilder.Append(fileUrl);
                        previousIndex = match.Index + match.Length;

                        _context.QuestionAssets.Add(new QuestionAsset
                        {
                            QuestionId = question.Id,
                            AssetType = "Image",
                            FileUrl = fileUrl,
                            FileName = fileName,
                            MimeType = mimeType,
                            AltText = "Hình minh họa",
                            Placement = field.Placement,
                            SortOrder = assetIndex++
                        });
                    }

                    replacedBuilder.Append(field.Value, previousIndex, field.Value.Length - previousIndex);
                    var replaced = replacedBuilder.ToString();

                    if (field.Name == "PassageText") question.PassageText = replaced;
                    else if (field.Name == "QuestionText") question.QuestionText = replaced;
                    else if (field.Name == "OptionA") question.OptionA = replaced;
                    else if (field.Name == "OptionB") question.OptionB = replaced;
                    else if (field.Name == "OptionC") question.OptionC = replaced;
                    else if (field.Name == "OptionD") question.OptionD = replaced;
                }
            }

            await _context.SaveChangesAsync();
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
                var assets = await _context.QuestionAssets
                    .Where(asset => questionIds.Contains(asset.QuestionId))
                    .ToListAsync();
                var assetRoot = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads", "tests", id.ToString());
                if (questions.Any())
                {
                    _context.Questions.RemoveRange(questions);
                }
                if (assets.Any())
                {
                    _context.QuestionAssets.RemoveRange(assets);
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
                if (Directory.Exists(assetRoot))
                {
                    Directory.Delete(assetRoot, recursive: true);
                }

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
