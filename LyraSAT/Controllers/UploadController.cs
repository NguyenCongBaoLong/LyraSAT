using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LyraSAT.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UploadController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;

        public UploadController(IWebHostEnvironment env)
        {
            _env = env;
        }

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

                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                var allowedExts = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".mp3", ".m4a", ".ogg", ".webm", ".wav", ".mp4" };
                if (!allowedExts.Contains(ext))
                {
                    // Fallback theo type nếu file name không có extension chuẩn
                    if (type == "audio") ext = ".webm";
                    else if (type == "video") ext = ".mp4";
                    else if (type == "image") ext = ".webp";
                    else return BadRequest(new { message = "Định dạng file không được hỗ trợ." });
                }

                var uploadDir = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads", "comments");
                if (!Directory.Exists(uploadDir))
                {
                    Directory.CreateDirectory(uploadDir);
                }

                var fileName = $"{Guid.NewGuid():N}{ext}";
                var filePath = Path.Combine(uploadDir, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var fileUrl = $"/uploads/comments/{fileName}";
                return Ok(new { url = fileUrl, fileName });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi lưu file: " + ex.Message });
            }
        }
    }
}
