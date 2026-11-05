using LyraSAT.Data;
using LyraSAT.DTOs;
using LyraSAT.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LyraSAT.Controllers
{
    /// <summary>
    /// Video chữa bài của đề. Một đề có thể có nhiều video,
    /// giáo viên gắn thêm bất cứ lúc nào sau khi đã giao đề.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class TestVideosController : ControllerBase
    {
        private const long MaxVideoBytes = 150L * 1024 * 1024;
        private static readonly string[] AllowedVideoExts =
            { ".mp4", ".m4v", ".webm", ".mov", ".ogv" };

        private readonly LyraSatDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<TestVideosController> _logger;

        public TestVideosController(
            LyraSatDbContext context,
            IWebHostEnvironment env,
            ILogger<TestVideosController> logger)
        {
            _context = context;
            _env = env;
            _logger = logger;
        }

        /// <summary>
        /// Kiểm tra link có phát được trong khung nhúng hay không.
        /// Chặn sớm link Google Docs/Slides/Folder vì những loại đó không nhúng được,
        /// đó chính là kiểu link làm khung video đen thui.
        /// </summary>
        private static string? ValidateVideoUrl(string? rawUrl)
        {
            var url = rawUrl?.Trim() ?? string.Empty;
            if (url.Length == 0)
            {
                return "Hãy dán link video hoặc tải file lên.";
            }

            // File đã upload nội bộ
            if (url.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "Link phải bắt đầu bằng http:// hoặc https://.";
            }

            if (url.Contains("docs.google.com") || url.Contains("slides.google.com") ||
                url.Contains("sheets.google.com") || url.Contains("drive.google.com/drive/"))
            {
                return "Link Google Docs/Slides/Drive folder không phát được trong khung video. " +
                       "Hãy mở file video bằng Drive rồi lấy link chia sẻ của chính file video đó.";
            }

            if (url.Contains("youtube.com") || url.Contains("youtu.be"))
            {
                return null;
            }

            // Link Drive dạng file: /file/d/<id>/view hoặc open?id=...
            if (url.Contains("drive.google.com"))
            {
                return null;
            }

            // Link video trực tiếp (.mp4, .webm...) hoặc dịch vụ nhúng phổ biến
            if (System.Text.RegularExpressions.Regex.IsMatch(url, @"\.(mp4|m4v|webm|mov|ogv)(\?.*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return null;
            }

            return "Link này chưa được hỗ trợ. Dùng link YouTube, Google Drive (file video) hoặc file mp4/webm.";
        }

        private async Task<bool> TestExists(int testId) =>
            await _context.Tests.AnyAsync(t => t.Id == testId);

        private async Task<int> NextSortOrder(int testId)
        {
            // Dung MaxAsync tren kieu nullable: DefaultIfEmpty khong dich duoc sang SQL voi Npgsql.
            var max = await _context.TestVideos
                .Where(v => v.TestId == testId)
                .Select(v => (int?)v.SortOrder)
                .MaxAsync();
            return (max ?? -1) + 1;
        }

        private async Task<object> BuildResponse(TestVideo video) => new
        {
            id = video.Id,
            testId = video.TestId,
            title = video.Title ?? "",
            url = video.Url,
            sortOrder = video.SortOrder,
            createdAt = video.CreatedAt
        };

        [HttpGet("test/{testId}")]
        public async Task<IActionResult> GetTestVideos(int testId)
        {
            if (!await TestExists(testId))
            {
                return NotFound(new { message = "Không tìm thấy bài test!" });
            }

            var videos = await _context.TestVideos
                .AsNoTracking()
                .Where(v => v.TestId == testId)
                .OrderBy(v => v.SortOrder)
                .ThenBy(v => v.Id)
                .ToListAsync();

            return Ok(videos.Select(v => new
            {
                id = v.Id,
                testId = v.TestId,
                title = v.Title ?? "",
                url = v.Url,
                sortOrder = v.SortOrder,
                createdAt = v.CreatedAt
            }).ToList());
        }

        [HttpPost("test/{testId}")]
        public async Task<IActionResult> AddLinkVideo(int testId, [FromBody] CreateTestVideoDto dto)
        {
            if (!await TestExists(testId))
            {
                return NotFound(new { message = "Không tìm thấy bài test!" });
            }

            var error = ValidateVideoUrl(dto?.Url);
            if (error != null)
            {
                return BadRequest(new { message = error });
            }

            var count = await _context.TestVideos.CountAsync(v => v.TestId == testId);
            var video = new TestVideo
            {
                TestId = testId,
                Title = string.IsNullOrWhiteSpace(dto?.Title) ? $"Video {count + 1}" : dto!.Title!.Trim(),
                Url = dto!.Url.Trim(),
                SortOrder = await NextSortOrder(testId)
            };

            _context.TestVideos.Add(video);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã thêm video chữa bài.", video = await BuildResponse(video) });
        }

        [HttpPost("test/{testId}/file")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(MaxVideoBytes + 5 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxVideoBytes + 5 * 1024 * 1024)]
        public async Task<IActionResult> AddFileVideo(int testId, IFormFile file, [FromForm] string? title)
        {
            if (!await TestExists(testId))
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

                var count = await _context.TestVideos.CountAsync(v => v.TestId == testId);
                var video = new TestVideo
                {
                    TestId = testId,
                    Title = string.IsNullOrWhiteSpace(title) ? $"Video {count + 1}" : title!.Trim(),
                    Url = $"/uploads/tests/{fileName}",
                    SortOrder = await NextSortOrder(testId)
                };

                _context.TestVideos.Add(video);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Đã tải video lên và gắn vào bài test.", video = await BuildResponse(video) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi lưu video chữa bài cho test {TestId}", testId);
                return StatusCode(500, new { message = "Lỗi khi lưu file video: " + ex.Message });
            }
        }

        [HttpPut("{videoId}")]
        public async Task<IActionResult> UpdateVideo(int videoId, [FromBody] CreateTestVideoDto dto)
        {
            var video = await _context.TestVideos.FirstOrDefaultAsync(v => v.Id == videoId);
            if (video == null)
            {
                return NotFound(new { message = "Không tìm thấy video!" });
            }

            if (dto?.Url != null)
            {
                var error = ValidateVideoUrl(dto.Url);
                if (error != null)
                {
                    return BadRequest(new { message = error });
                }
                video.Url = dto.Url.Trim();
            }

            if (dto?.Title != null)
            {
                video.Title = dto.Title.Trim();
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Đã cập nhật video.", video = await BuildResponse(video) });
        }

        [HttpDelete("{videoId}")]
        public async Task<IActionResult> DeleteVideo(int videoId)
        {
            var video = await _context.TestVideos.FirstOrDefaultAsync(v => v.Id == videoId);
            if (video == null)
            {
                return NotFound(new { message = "Không tìm thấy video!" });
            }

            _context.TestVideos.Remove(video);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã gỡ video khỏi bài test." });
        }
    }
}
