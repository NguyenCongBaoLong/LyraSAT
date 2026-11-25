using LyraSAT.Data;
using LyraSAT.DTOs;
using LyraSAT.Models;
using LyraSAT.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
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
        private readonly LyraSatDbContext _context;
        private readonly ISupabaseStorageService _storage;
        private readonly ILogger<TestVideosController> _logger;

        public TestVideosController(
            LyraSatDbContext context,
            ISupabaseStorageService storage,
            ILogger<TestVideosController> logger)
        {
            _context = context;
            _storage = storage;
            _logger = logger;
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

        private static object BuildResponse(TestVideo video) => new
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

            return Ok(videos.Select(BuildResponse).ToList());
        }

        [HttpPost("test/{testId}")]
        public async Task<IActionResult> AddLinkVideo(int testId, [FromBody] CreateTestVideoDto dto)
        {
            var test = await _context.Tests.FirstOrDefaultAsync(t => t.Id == testId);
            if (test == null)
            {
                return NotFound(new { message = "Không tìm thấy bài test!" });
            }

            var normalizedUrl = VideoLinkValidator.Normalize(dto?.Url);
            var error = VideoLinkValidator.Validate(normalizedUrl);
            if (error != null)
            {
                return BadRequest(new { message = error });
            }

            var count = await _context.TestVideos.CountAsync(v => v.TestId == testId);
            var video = new TestVideo
            {
                TestId = testId,
                Title = string.IsNullOrWhiteSpace(dto?.Title) ? $"Video {count + 1}" : dto!.Title!.Trim(),
                Url = normalizedUrl,
                SortOrder = await NextSortOrder(testId)
            };

            _context.TestVideos.Add(video);
            if (string.IsNullOrWhiteSpace(test.VideoExplanationUrl))
            {
                test.VideoExplanationUrl = video.Url;
            }
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Đã thêm video chữa bài.",
                video = BuildResponse(video),
                videoExplanationUrl = test.VideoExplanationUrl ?? ""
            });
        }

        [HttpPost("test/{testId}/file")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(VideoLinkValidator.MaxVideoBytes + 5 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = VideoLinkValidator.MaxVideoBytes + 5 * 1024 * 1024)]
        public async Task<IActionResult> AddFileVideo(int testId, IFormFile file, [FromForm] string? title)
        {
            var test = await _context.Tests.FirstOrDefaultAsync(t => t.Id == testId);
            if (test == null)
            {
                return NotFound(new { message = "Không tìm thấy bài test!" });
            }

            var fileErr = VideoLinkValidator.ValidateVideoFile(file, out var ext, out var contentType);
            if (fileErr != null)
            {
                return BadRequest(new { message = fileErr });
            }

            string? uploadedUrl = null;
            try
            {
                var objectPath = $"tests/{testId}/{Guid.NewGuid():N}{ext}";
                await using var stream = file.OpenReadStream();
                uploadedUrl = await _storage.UploadStreamAsync(
                    stream,
                    file.Length,
                    objectPath,
                    contentType,
                    SupabaseStorageService.MediaBucket,
                    HttpContext.RequestAborted);

                var count = await _context.TestVideos.CountAsync(v => v.TestId == testId);
                var video = new TestVideo
                {
                    TestId = testId,
                    Title = string.IsNullOrWhiteSpace(title) ? $"Video {count + 1}" : title!.Trim(),
                    Url = uploadedUrl,
                    SortOrder = await NextSortOrder(testId)
                };

                _context.TestVideos.Add(video);
                if (string.IsNullOrWhiteSpace(test.VideoExplanationUrl))
                {
                    test.VideoExplanationUrl = uploadedUrl;
                }
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Đã tải video lên lưu trữ đám mây và gắn vào bài test.",
                    video = BuildResponse(video),
                    videoExplanationUrl = test.VideoExplanationUrl ?? ""
                });
            }
            catch (StorageFileTooLargeException)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new
                {
                    message = "File video vượt giới hạn dung lượng lưu trữ đám mây (thường 50MB). Hãy nén nhỏ hơn hoặc đăng lên YouTube / Google Drive (công khai hay không công khai đều được) rồi dán link."
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
                        _logger.LogWarning(cleanupEx, "Không dọn được video mồ côi {Url} sau khi lưu DB lỗi", uploadedUrl);
                    }
                }
                _logger.LogError(ex, "Lỗi lưu video chữa bài cho test {TestId}", testId);
                return StatusCode(500, new { message = "Lỗi khi lưu file video: " + ex.Message });
            }
        }

        [HttpPut("{videoId}")]
        public async Task<IActionResult> UpdateVideo(int videoId, [FromBody] UpdateTestVideoDto? dto)
        {
            var video = await _context.TestVideos.FirstOrDefaultAsync(v => v.Id == videoId);
            if (video == null)
            {
                return NotFound(new { message = "Không tìm thấy video!" });
            }

            var oldUrl = video.Url;

            if (dto?.Url != null)
            {
                var normalizedUrl = VideoLinkValidator.Normalize(dto.Url);
                var error = VideoLinkValidator.Validate(normalizedUrl);
                if (error != null)
                {
                    return BadRequest(new { message = error });
                }
                video.Url = normalizedUrl;
            }

            if (dto?.Title != null)
            {
                video.Title = dto.Title.Trim();
            }

            var test = await _context.Tests.FirstOrDefaultAsync(t => t.Id == video.TestId);
            if (test != null &&
                !string.Equals(oldUrl, video.Url, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(test.VideoExplanationUrl, oldUrl, StringComparison.OrdinalIgnoreCase))
            {
                test.VideoExplanationUrl = video.Url;
            }

            await _context.SaveChangesAsync();

            if (!string.Equals(oldUrl, video.Url, StringComparison.OrdinalIgnoreCase))
            {
                await MediaUrlCleaner.DeleteIfUnusedAsync(_context, _storage, oldUrl);
            }

            return Ok(new
            {
                message = "Đã cập nhật video.",
                video = BuildResponse(video),
                videoExplanationUrl = test?.VideoExplanationUrl ?? ""
            });
        }

        [HttpDelete("{videoId}")]
        public async Task<IActionResult> DeleteVideo(int videoId)
        {
            var video = await _context.TestVideos.FirstOrDefaultAsync(v => v.Id == videoId);
            if (video == null)
            {
                return NotFound(new { message = "Không tìm thấy video!" });
            }

            var testId = video.TestId;
            var oldUrl = video.Url;
            _context.TestVideos.Remove(video);

            var test = await _context.Tests.FirstOrDefaultAsync(t => t.Id == testId);
            if (test != null && string.Equals(test.VideoExplanationUrl, oldUrl, StringComparison.OrdinalIgnoreCase))
            {
                var nextVideo = await _context.TestVideos
                    .AsNoTracking()
                    .Where(v => v.TestId == testId && v.Id != videoId)
                    .OrderBy(v => v.SortOrder)
                    .ThenBy(v => v.Id)
                    .FirstOrDefaultAsync();
                test.VideoExplanationUrl = nextVideo?.Url;
            }

            await _context.SaveChangesAsync();

            await MediaUrlCleaner.DeleteIfUnusedAsync(_context, _storage, oldUrl);

            return Ok(new
            {
                message = "Đã gỡ video khỏi bài test.",
                videoExplanationUrl = test?.VideoExplanationUrl ?? ""
            });
        }
    }
}
