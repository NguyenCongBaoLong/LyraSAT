using LyraSAT.Data;
using LyraSAT.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.XWPF.UserModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LyraSAT.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VocabulariesController : ControllerBase
    {
        private readonly LyraSatDbContext _context;

        public VocabulariesController(LyraSatDbContext context)
        {
            _context = context;
        }

        [HttpGet("class/{classCode}/series")]
        public async Task<IActionResult> GetVocabSeriesByClass(
            string classCode,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 8)
        {
            try
            {
                var seriesList = await _context.Vocabularies
                    .Where(v => v.ClassCode == classCode || v.ClassCode == null)
                    .Select(v => v.SeriesName)
                    .Distinct()
                    .OrderBy(seriesName => seriesName)
                    .ToPagedResultAsync(page, pageSize);

                return Ok(seriesList);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        [HttpGet("class/{classCode}/series/{seriesName}")]
        public async Task<IActionResult> GetVocabulariesByClassAndSeries(
            string classCode,
            string seriesName,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            try
            {
                var vocabList = await _context.Vocabularies
                    .Where(v => (v.ClassCode == classCode || v.ClassCode == null) && v.SeriesName == seriesName)
                    .OrderBy(v => v.Id)
                    .Select(v => new
                    {
                        id = v.Id,
                        word = v.Word,
                        definition = v.Definition,
                        example = v.Example,
                        audioUrl = v.AudioUrl
                    })
                    .ToPagedResultAsync(page, pageSize);

                return Ok(vocabList);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateVocabulary([FromBody] Vocabulary vocab)
        {
            try
            {
                if (string.IsNullOrEmpty(vocab.Word) || string.IsNullOrEmpty(vocab.Definition))
                {
                    return BadRequest(new { message = "Từ vựng và định nghĩa không được để trống." });
                }

                _context.Vocabularies.Add(vocab);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Thêm từ vựng thành công!", data = vocab });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // BỔ SUNG: API upload bộ từ vựng qua file Word (.docx)
        [HttpPost("upload-vocab-file")]
        public async Task<IActionResult> UploadVocabFile(
            [FromForm] string seriesName,
            [FromForm] string classCode,
            IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "Vui lòng chọn file Word (.docx) để tải lên!" });
            }

            if (!file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Hệ thống chỉ hỗ trợ định dạng file Word (.docx)!" });
            }

            if (string.IsNullOrEmpty(seriesName))
            {
                return BadRequest(new { message = "Tên bộ từ vựng (Series Name) không được để trống!" });
            }

            try
            {
                using var stream = file.OpenReadStream();
                var document = new XWPFDocument(stream);

                var paragraphs = document.Paragraphs
                    .Select(p => p.ParagraphText.Trim())
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList();

                var vocabList = new List<Vocabulary>();

                foreach (var line in paragraphs)
                {
                    // Hỗ trợ tách các trường bằng dấu gạch đứng (|), dấu phẩy (,) hoặc chấm phẩy (;)
                    char separator = line.Contains('|') ? '|' : line.Contains(';') ? ';' : ',';
                    var parts = line.Split(separator).Select(p => p.Trim()).ToArray();

                    if (parts.Length >= 2 && !string.IsNullOrEmpty(parts[0]))
                    {
                        vocabList.Add(new Vocabulary
                        {
                            ClassCode = classCode,
                            SeriesName = seriesName,
                            Word = parts[0],
                            Definition = parts[1],
                            Example = parts.Length > 2 ? parts[2] : ""
                        });
                    }
                }

                if (vocabList.Count == 0)
                {
                    return BadRequest(new { message = "Không tìm thấy từ vựng hợp lệ trong file Word. Vui lòng kiểm tra lại cấu trúc (Từ | Nghĩa | Ví dụ)!" });
                }

                _context.Vocabularies.AddRange(vocabList);
                await _context.SaveChangesAsync();

                return Ok(new { message = $"Đã tải lên thành công {vocabList.Count} từ vựng từ file Word!", count = vocabList.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi xử lý file Word: " + ex.Message });
            }
        }

        [HttpGet("classes")]
        public async Task<IActionResult> GetAllClasses(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 8)
        {
            try
            {
                var classList = await _context.Vocabularies
                    .Where(v => v.ClassCode != null)
                    .Select(v => v.ClassCode)
                    .Distinct()
                    .OrderBy(classCode => classCode)
                    .ToPagedResultAsync(page, pageSize);

                return Ok(classList);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }
    }
}