using LyraSAT.Data;
using LyraSAT.Models;
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

        public TestsController(LyraSatDbContext context)
        {
            _context = context;
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
            var tests = _context.Tests
                .AsNoTracking()
                .Where(t => t.ClassCode == classCode);

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
            var secureQuestions = test.Questions.Select(q => new
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

            _context.Tests.Add(model);
            await _context.SaveChangesAsync();
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

                // 1. Tạo bài test mới trong cơ sở dữ liệu
                var newTest = new Test
                {
                    Title = title,
                    ClassCode = classCode,
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

                var extractedLines = new List<string>();

                foreach (var element in document.BodyElements)
                {
                    if (element is XWPFParagraph paragraph)
                    {
                        AddParagraphLines(extractedLines, paragraph.ParagraphText);
                    }
                    else if (element is XWPFTable table)
                    {
                        // Chuyển đổi bảng Word thành chuỗi HTML chuẩn
                        var htmlTable = new System.Text.StringBuilder();
                        htmlTable.Append("<div class='passage-table-wrapper'><table class='passage-table'><tbody>");

                        foreach (var row in table.Rows)
                        {
                            htmlTable.Append("<tr>");
                            foreach (var cell in row.GetTableCells())
                            {
                                string cellText = System.Net.WebUtility.HtmlEncode(cell.GetText());
                                htmlTable.Append($"<td>{cellText}</td>");
                            }
                            htmlTable.Append("</tr>");
                        }
                        htmlTable.Append("</tbody></table></div>");

                        extractedLines.Add(htmlTable.ToString());
                    }
                }

                // 3. Bóc tách câu hỏi SAT
                var questions = ParseSatWordFile(extractedLines, newTest.Id);

                // KIỂM TRA: Nếu không có câu hỏi hợp lệ -> Xóa bài test vừa tạo và báo lỗi
                if (questions.Count == 0)
                {
                    _context.Tests.Remove(newTest);
                    await _context.SaveChangesAsync();
                    return BadRequest(new { message = "File Word không đúng cấu trúc định dạng câu hỏi SAT (Thiếu các từ khóa Text:, Question:, hoặc các đáp án A, B, C, D)!" });
                }

                _context.Questions.AddRange(questions);
                await _context.SaveChangesAsync();

                return Ok(new { message = $"Tải lên và xử lý thành công {questions.Count} câu hỏi từ file Word!", testId = newTest.Id });
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null ? " -> " + ex.InnerException.Message : "";
                return StatusCode(500, new { message = "Lỗi khi xử lý file Word: " + ex.Message + innerMsg });
            }
        }

        private List<Question> ParseSatWordFile(List<string> lines, int testId)
        {
            var questions = new List<Question>();
            Question? currentQ = null;
            var passageLines = new List<string>();
            var questionLines = new List<string>();
            bool isReadingPassage = true;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                {
                    if (currentQ != null)
                    {
                        if (isReadingPassage) passageLines.Add(rawLine);
                        else questionLines.Add(rawLine);
                    }
                    continue;
                }

                if (line.StartsWith("Text:", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentQ != null)
                    {
                        currentQ.PassageText = JoinPreservingLineBreaks(passageLines);
                        currentQ.QuestionText = JoinPreservingLineBreaks(questionLines);

                        // Kiểm tra an toàn: Bắt buộc phải có QuestionText và OptionA để tránh lỗi cột NOT NULL của DB
                        if (!string.IsNullOrEmpty(currentQ.QuestionText) && !string.IsNullOrEmpty(currentQ.OptionA))
                        {
                            questions.Add(currentQ);
                        }
                    }

                    currentQ = new Question { TestId = testId };
                    passageLines.Clear();
                    questionLines.Clear();
                    isReadingPassage = true;

                    string contentAfterColon = line.Substring(5).TrimStart();
                    if (!string.IsNullOrEmpty(contentAfterColon))
                    {
                        passageLines.Add(contentAfterColon);
                    }
                }
                else if (line.StartsWith("Question:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Câu:", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentQ == null)
                    {
                        currentQ = new Question { TestId = testId };
                    }

                    isReadingPassage = false;

                    string contentAfterColon = line.Substring(line.IndexOf(':') + 1).TrimStart();
                    if (!string.IsNullOrEmpty(contentAfterColon))
                    {
                        questionLines.Add(contentAfterColon);
                    }
                }
                else if (currentQ != null)
                {
                    if (line.Contains("A:") && line.Contains("B:") && line.Contains("C:") && line.Contains("D:"))
                    {
                        ExtractAllOptionsInline(currentQ, line);
                    }
                    else if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^[A-D][:\.]"))
                    {
                        char optKey = char.ToUpper(line[0]);
                        string optVal = line.Substring(2).TrimStart();

                        if (optKey == 'A') currentQ.OptionA = optVal;
                        else if (optKey == 'B') currentQ.OptionB = optVal;
                        else if (optKey == 'C') currentQ.OptionC = optVal;
                        else if (optKey == 'D') currentQ.OptionD = optVal;
                    }
                    else if (line.StartsWith("ANSWER", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Đáp án", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(new[] { ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 1)
                        {
                            currentQ.CorrectAnswer = parts.Last().Trim().ToUpper();
                        }
                    }
                    else if (line.StartsWith("VIDEO", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Link video", StringComparison.OrdinalIgnoreCase))
                    {
                        var colonIdx = line.IndexOf(':');
                        if (colonIdx >= 0 && colonIdx < line.Length - 1)
                        {
                            currentQ.VideoExplanationUrl = line.Substring(colonIdx + 1).Trim();
                        }
                    }
                    else
                    {
                        if (isReadingPassage)
                        {
                            passageLines.Add(line);
                        }
                        else
                        {
                            questionLines.Add(line);
                        }
                    }
                }
                else
                {
                    passageLines.Add(line);
                }
            }

            if (currentQ != null)
            {
                currentQ.PassageText = JoinPreservingLineBreaks(passageLines);
                currentQ.QuestionText = JoinPreservingLineBreaks(questionLines);

                if (!string.IsNullOrEmpty(currentQ.QuestionText) && !string.IsNullOrEmpty(currentQ.OptionA))
                {
                    questions.Add(currentQ);
                }
            }

            return questions;
        }

        private static void AddParagraphLines(List<string> lines, string? paragraphText)
        {
            if (paragraphText == null) return;

            var normalizedText = paragraphText.Replace("\r\n", "\n").Replace('\r', '\n');
            lines.AddRange(normalizedText.Split('\n', StringSplitOptions.None));
        }

        private static string JoinPreservingLineBreaks(List<string> lines)
        {
            return string.Join("\n", lines).Trim('\r', '\n');
        }

        private void ExtractAllOptionsInline(Question q, string text)
        {
            try
            {
                var parts = System.Text.RegularExpressions.Regex.Split(text, @"(?=[A-D][:\.])");
                foreach (var p in parts)
                {
                    string trimmed = p.TrimStart();
                    if (trimmed.StartsWith("A:") || trimmed.StartsWith("A.")) q.OptionA = trimmed.Substring(2).Trim();
                    else if (trimmed.StartsWith("B:") || trimmed.StartsWith("B.")) q.OptionB = trimmed.Substring(2).Trim();
                    else if (trimmed.StartsWith("C:") || trimmed.StartsWith("C.")) q.OptionC = trimmed.Substring(2).Trim();
                    else if (trimmed.StartsWith("D:") || trimmed.StartsWith("D.")) q.OptionD = trimmed.Substring(2).Trim();
                }
            }
            catch { }
        }

        [HttpGet("class/{classCode}/results")]
        public async Task<IActionResult> GetTestResultsByClass(
            string classCode,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            var results = await _context.Tests
                .AsNoTracking()
                .Where(t => t.ClassCode == classCode)
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
            var students = await _context.Users
                .AsNoTracking()
                .Where(u => u.ClassCode == classCode && u.Role == "Student")
                .OrderBy(u => u.FullName)
                .ThenBy(u => u.Id)
                .Select(u => new
                {
                    u.Id,
                    u.FullName,
                    u.ClassCode,
                    TuitionPaid = u.TuitionStatus ?? false
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
                    questions = t.Questions.Select(q => new
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
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 8)
        {
            try
            {
                var submissions = await _context.Submissions
                    .Where(s => s.TestId == testId)
                    .AsNoTracking()
                    .OrderByDescending(s => s.SubmittedAt)
                    .ThenByDescending(s => s.Id)
                    .Include(s => s.Student)
                    .Select(s => new
                    {
                        id = s.Id,
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
                var student = await _context.Users.FirstOrDefaultAsync(u => u.Id == studentId && u.ClassCode == classCode);
                if (student == null)
                {
                    return NotFound(new { message = "Không tìm thấy học sinh trong lớp này." });
                }

                student.ClassCode = null;
                await _context.SaveChangesAsync();

                return Ok(new { message = $"Đã gỡ học viên {student.FullName} ra khỏi lớp {classCode} thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi gỡ học sinh khỏi lớp: " + ex.Message });
            }
        }
    }
}