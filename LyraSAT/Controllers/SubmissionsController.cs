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
    public class SubmissionsController : ControllerBase
    {
        private readonly LyraSatDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly ILogger<SubmissionsController> _logger;

        public SubmissionsController(
            LyraSatDbContext context,
            INotificationService notificationService,
            ILogger<SubmissionsController> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _logger = logger;
        }

        // Nộp bài thi và chấm điểm tự động
        [HttpPost]
        public async Task<IActionResult> SubmitTest([FromBody] SubmitDto model)
        {
            var test = await _context.Tests
                .Include(t => t.Questions)
                .FirstOrDefaultAsync(t => t.Id == model.TestId);

            if (test == null) return NotFound(new { message = "Bài test không tồn tại!" });

            // Chặn nộp bài với studentId không tồn tại (tránh lỗi FK 500)
            var studentExists = await _context.Users.AnyAsync(u => u.Id == model.StudentId);
            if (!studentExists)
                return BadRequest(new { message = "Tài khoản học sinh không tồn tại. Vui lòng đăng nhập lại!" });

            int totalQuestions = test.Questions.Count;
            int correctCount = 0;
            var submissionDetails = new List<SubmissionDetail>();

            foreach (var kvp in model.Answers)
            {
                int questionId = kvp.Key;
                string selectedAnswer = kvp.Value ?? "";

                var question = test.Questions.FirstOrDefault(q => q.Id == questionId);
                bool isCorrect = false;

                if (question != null && !string.IsNullOrEmpty(question.CorrectAnswer))
                {
                    if (question.CorrectAnswer.Trim().Equals(selectedAnswer.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        correctCount++;
                        isCorrect = true;
                    }
                }

                submissionDetails.Add(new SubmissionDetail
                {
                    QuestionId = questionId,
                    SelectedAnswer = selectedAnswer,
                    IsCorrect = isCorrect
                });
            }

            double scorePercent = totalQuestions > 0 ? Math.Round((double)correctCount / totalQuestions * 100, 0) : 0;

            var submission = new Submission
            {
                StudentId = model.StudentId,
                TestId = model.TestId,
                CorrectCount = correctCount,
                TotalQuestions = totalQuestions,
                Score = scorePercent,
                TimeSpent = model.TimeSpent,
                SubmittedAt = DateTime.UtcNow,
                Status = "Completed",
                SubmissionDetails = submissionDetails
            };

            _context.Submissions.Add(submission);
            await _context.SaveChangesAsync();

            // --- Gửi Thông báo Nộp bài cho Toàn bộ Giáo viên phụ trách lớp (2-4 giáo viên) ---
            try
            {
                var student = await _context.Users.FindAsync(model.StudentId);

                await _notificationService.NotifyTestSubmittedAsync(
                    studentId: model.StudentId,
                    studentName: student?.FullName,
                    classCode: test.ClassCode ?? string.Empty,
                    testId: test.Id,
                    testTitle: test.Title,
                    correctCount: correctCount,
                    totalQuestions: totalQuestions,
                    scorePercent: scorePercent,
                    timeSpentSeconds: model.TimeSpent);
            }
            catch (Exception notifEx)
            {
                _logger.LogError(notifEx, "Lỗi gửi thông báo nộp bài cho test {TestId} (học sinh {StudentId})", test.Id, model.StudentId);
            }

            return Ok(new
            {
                message = "Nộp bài thành công!",
                score = scorePercent,
                correctCount = correctCount,
                totalQuestions = totalQuestions,
                rewardPoints = correctCount,
                submissionId = submission.Id
            });
        }

        // Xem lịch sử làm bài và chi tiết đáp án
        [HttpGet("history/{studentId}")]
        public async Task<IActionResult> GetStudentHistory(
            int studentId,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 8)
        {
            var historyQuery = _context.Submissions
                .AsNoTracking()
                .Where(s => s.StudentId == studentId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                historyQuery = historyQuery.Where(s => s.Test != null && EF.Functions.ILike(s.Test.Title, $"%{search}%"));
            }

            var history = await historyQuery
                .OrderByDescending(s => s.SubmittedAt)
                .ThenByDescending(s => s.Id)
                .Include(s => s.Test)
                .Select(s => new
                {
                    s.Id,
                    TestTitle = s.Test.Title,
                    s.CorrectCount,
                    TotalQuestions = s.TotalQuestions,
                    s.Score,
                    s.TimeSpent,
                    s.SubmittedAt,
                    VideoExplanationUrl = s.Test != null ? s.Test.VideoExplanationUrl : null
                })
                .ToPagedResultAsync(page, pageSize);

            return Ok(history);
        }

        // Lấy chi tiết bài làm để xem lại (Review)
        [HttpGet("{submissionId}/review")]
        public async Task<IActionResult> ReviewSubmission(int submissionId)
        {
            var submission = await _context.Submissions
                .Include(s => s.Test)
                .ThenInclude(t => t.Questions)
                .Include(s => s.SubmissionDetails)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null)
                return NotFound(new { message = "Không tìm thấy bài nộp!" });

            var reviewData = new
            {
                submissionId = submission.Id,
                testTitle = submission.Test?.Title ?? "Bài test",
                videoExplanationUrl = submission.Test?.VideoExplanationUrl ?? "",
                correctCount = submission.CorrectCount,
                totalQuestions = submission.TotalQuestions,
                score = submission.Score,
                questions = submission.Test?.Questions.Select(q =>
                {
                    var detail = submission.SubmissionDetails.FirstOrDefault(sd => sd.QuestionId == q.Id);
                    return new
                    {
                        id = q.Id,
                        questionText = q.QuestionText ?? "",
                        passageText = q.PassageText ?? "", // Đã sửa từ q.Explanation thành q.PassageText chuẩn xác
                        optionA = q.OptionA ?? "",
                        optionB = q.OptionB ?? "",
                        optionC = q.OptionC ?? "",
                        optionD = q.OptionD ?? "",
                        selectedAnswer = detail?.SelectedAnswer ?? "", // Đáp án học sinh chọn
                        correctAnswer = q.CorrectAnswer ?? "",          // Đáp án đúng thực tế
                        videoExplanationUrl = q.VideoExplanationUrl ?? "",
                        isCorrect = detail?.IsCorrect ?? false
                    };
                }).ToList()
            };

            return Ok(reviewData);
        }
    }
}