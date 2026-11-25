using LyraSAT.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Threading.Tasks;

namespace LyraSAT.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UploadController : ControllerBase
    {
        private readonly ISupabaseStorageService _storage;
        private readonly ILogger<UploadController> _logger;

        public UploadController(
            ISupabaseStorageService storage,
            ILogger<UploadController> logger)
        {
            _storage = storage;
            _logger = logger;
        }

        private static string? GetAttachmentContentType(string ext) => ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".webm" => "audio/webm",
            ".wav" => "audio/wav",
            ".mp4" => "video/mp4",
            _ => null
        };

        [HttpPost("attachment")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadAttachment(IFormFile file, [FromForm] string? type)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new { message = "Không tìm thấy file tải lên." });
                }

                // Giới hạn 15MB
                if (file.Length > 15 * 1024 * 1024)
                {
                    return BadRequest(new { message = "File vượt quá giới hạn 15MB." });
                }

                var ext = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
                var contentType = GetAttachmentContentType(ext);
                if (contentType == null)
                {
                    // Fallback theo type nếu file name không có extension chuẩn
                    if (type == "audio") ext = ".webm";
                    else if (type == "video") ext = ".mp4";
                    else if (type == "image") ext = ".webp";
                    else return BadRequest(new { message = "Định dạng file không được hỗ trợ." });

                    contentType = GetAttachmentContentType(ext) ?? "application/octet-stream";
                }

                if (ext == ".webm" && type == "video")
                {
                    contentType = "video/webm";
                }

                var fileName = $"{Guid.NewGuid():N}{ext}";
                var objectPath = $"comments/{fileName}";
                await using var stream = file.OpenReadStream();
                var fileUrl = await _storage.UploadStreamAsync(
                    stream,
                    file.Length,
                    objectPath,
                    contentType,
                    SupabaseStorageService.MediaBucket,
                    HttpContext.RequestAborted);

                return Ok(new { url = fileUrl, fileName });
            }
            catch (StorageFileTooLargeException ex)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi lưu file đính kèm");
                return StatusCode(500, new { message = "Lỗi khi lưu file: " + ex.Message });
            }
        }
    }
}
