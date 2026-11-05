using LyraSAT.Data;
using LyraSAT.DTOs;
using LyraSAT.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace LyraSAT.Controllers
{
    /// <summary>
    /// Thao tác trên từng câu hỏi. Hiện chỉ có gắn/gỡ link video chữa riêng cho một câu:
    /// học sinh bấm vào câu nào trong màn xem lại bài thì mới thấy link video của câu đó,
    /// và chỉ hiện ở màn xem lại, không hiện trong lúc đang làm bài.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class QuestionsController : ControllerBase
    {
        private readonly LyraSatDbContext _context;

        public QuestionsController(LyraSatDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Gắn link video chữa bài cho một câu hỏi. Gửi url rỗng để gỡ video.
        /// </summary>
        [HttpPut("{questionId}/video")]
        public async Task<IActionResult> SetQuestionVideo(int questionId, [FromBody] QuestionVideoDto dto)
        {
            var question = await _context.Questions.FirstOrDefaultAsync(q => q.Id == questionId);
            if (question == null)
            {
                return NotFound(new { message = "Không tìm thấy câu hỏi!" });
            }

            var url = dto?.Url?.Trim() ?? string.Empty;

            if (url.Length == 0)
            {
                question.VideoExplanationUrl = null;
                await _context.SaveChangesAsync();
                return Ok(new { message = "Đã gỡ video của câu hỏi.", videoExplanationUrl = "" });
            }

            var error = VideoLinkValidator.Validate(url);
            if (error != null)
            {
                return BadRequest(new { message = error });
            }

            question.VideoExplanationUrl = url;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã gắn video cho câu hỏi.", videoExplanationUrl = url });
        }

        /// <summary>
        /// Đọc link video chữa của một câu (dùng khi cần kiểm tra nhanh).
        /// </summary>
        [HttpGet("{questionId}/video")]
        public async Task<IActionResult> GetQuestionVideo(int questionId)
        {
            var question = await _context.Questions
                .AsNoTracking()
                .Where(q => q.Id == questionId)
                .Select(q => new { q.Id, q.VideoExplanationUrl })
                .FirstOrDefaultAsync();

            if (question == null)
            {
                return NotFound(new { message = "Không tìm thấy câu hỏi!" });
            }

            return Ok(new
            {
                questionId = question.Id,
                videoExplanationUrl = question.VideoExplanationUrl ?? ""
            });
        }
    }
}
