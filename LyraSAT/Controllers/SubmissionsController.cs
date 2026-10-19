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
    [Route("api/[controller]")]
    [ApiController]
    public class SubmissionsController : ControllerBase
    {
        private readonly LyraSatDbContext _context;

        public SubmissionsController(LyraSatDbContext context)
        {
            _context = context;
        }

        // Nộp bài thi và chấm điểm tự động
        [HttpPost]
        public async Task<IActionResult> SubmitTest([FromBody] SubmitDto model)
        {
            var test = await _context.Tests
                .Include(t => t.Questions)
                .FirstOrDefaultAsync(t => t.Id == model.TestId);

            if (test == null) return NotFound(new { message = "Bài test không tồn tại!" });

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
        public async Task<IActionResult> GetStudentHistory(int studentId)
        {
            var history = await _context.Submissions
                .Where(s => s.StudentId == studentId)
                .Include(s => s.Test)
                .Select(s => new
                {
                    s.Id,
                    TestTitle = s.Test.Title,
                    s.CorrectCount,
                    TotalQuestions = s.TotalQuestions,
                    s.Score,
                    s.TimeSpent,
                    s.SubmittedAt
                })
                .ToListAsync();

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
                        isCorrect = detail?.IsCorrect ?? false
                    };
                }).ToList()
            };

            return Ok(reviewData);
        }
    }

    public class SubmitDto
    {
        public int StudentId { get; set; }
        public int TestId { get; set; }
        public int TimeSpent { get; set; } // Tính bằng giây
        public Dictionary<int, string> Answers { get; set; } = new();
    }
}