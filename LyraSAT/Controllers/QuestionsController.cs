using LyraSAT.Data;
using LyraSAT.DTOs;
using LyraSAT.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace LyraSAT.Controllers
{
    /// <summary>
    /// Thao tác trên từng câu hỏi. Hỗ trợ gắn link hoặc tải file video chữa riêng cho một câu:
    /// học sinh bấm vào câu nào trong màn xem lại bài thì mới thấy video của câu đó,
    /// và chỉ hiện ở màn xem lại, không hiện trong lúc đang làm bài.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class QuestionsController : ControllerBase
    {
        private readonly LyraSatDbContext _context;
        private readonly ISupabaseStorageService _storage;
        private readonly ILogger<QuestionsController> _logger;

        public QuestionsController(
            LyraSatDbContext context,
            ISupabaseStorageService storage,
            ILogger<QuestionsController> logger)
        {
            _context = context;
            _storage = storage;
            _logger = logger;
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

            var oldUrl = question.VideoExplanationUrl;
            var url = VideoLinkValidator.Normalize(dto?.Url);

            if (url.Length == 0)
            {
                question.VideoExplanationUrl = null;
                await _context.SaveChangesAsync();
                await MediaUrlCleaner.DeleteIfUnusedAsync(_context, _storage, oldUrl);
                return Ok(new { message = "Đã gỡ video của câu hỏi.", videoExplanationUrl = "" });
            }

            var error = VideoLinkValidator.Validate(url);
            if (error != null)
            {
                return BadRequest(new { message = error });
            }

            question.VideoExplanationUrl = url;
            await _context.SaveChangesAsync();

            if (!string.Equals(oldUrl, url, StringComparison.OrdinalIgnoreCase))
            {
                await MediaUrlCleaner.DeleteIfUnusedAsync(_context, _storage, oldUrl);
            }

            return Ok(new { message = "Đã gắn video cho câu hỏi.", videoExplanationUrl = url });
        }

        /// <summary>
        /// Tải file video chữa riêng cho một câu hỏi lên lưu trữ đám mây và gắn vào câu hỏi.
        /// </summary>
        [HttpPost("{questionId}/video-file")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(VideoLinkValidator.MaxVideoBytes + 5 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = VideoLinkValidator.MaxVideoBytes + 5 * 1024 * 1024)]
        public async Task<IActionResult> UploadQuestionVideo(int questionId, IFormFile file)
        {
            var question = await _context.Questions.FirstOrDefaultAsync(q => q.Id == questionId);
            if (question == null)
            {
                return NotFound(new { message = "Không tìm thấy câu hỏi!" });
            }

            var fileErr = VideoLinkValidator.ValidateVideoFile(file, out var ext, out var contentType);
            if (fileErr != null)
            {
                return BadRequest(new { message = fileErr });
            }

            string? uploadedUrl = null;
            try
            {
                var objectPath = $"questions/{questionId}/{Guid.NewGuid():N}{ext}";
                await using var stream = file.OpenReadStream();
                uploadedUrl = await _storage.UploadStreamAsync(
                    stream,
                    file.Length,
                    objectPath,
                    contentType,
                    SupabaseStorageService.MediaBucket,
                    HttpContext.RequestAborted);

                var oldUrl = question.VideoExplanationUrl;
                question.VideoExplanationUrl = uploadedUrl;
                await _context.SaveChangesAsync();

                if (!string.Equals(oldUrl, uploadedUrl, StringComparison.OrdinalIgnoreCase))
                {
                    await MediaUrlCleaner.DeleteIfUnusedAsync(_context, _storage, oldUrl);
                }

                return Ok(new { message = "Đã tải video lên lưu trữ đám mây và gắn cho câu hỏi.", videoExplanationUrl = uploadedUrl });
            }
            catch (StorageFileTooLargeException)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new
                {
                    message = "File video vượt giới hạn dung lượng lưu trữ đám mây (thường 50MB). Hãy nén nhỏ hơn hoặc đăng lên YouTube / Google Drive rồi dán link."
                });
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(uploadedUrl))
                {
                    try
                    {
                        await MediaUrlCleaner.DeleteIfUnusedAsync(_context, _storage, uploadedUrl);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning(cleanupEx, "Không dọn được video câu hỏi mồ côi {Url}", uploadedUrl);
                    }
                }
                _logger.LogError(ex, "Lỗi lưu video cho câu hỏi {QuestionId}", questionId);
                return StatusCode(500, new { message = "Lỗi khi lưu file video: " + ex.Message });
            }
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
