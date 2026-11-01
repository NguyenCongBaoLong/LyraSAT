using LyraSAT.Data;
using LyraSAT.Models;
using LyraSAT.Services;
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
                var lowerClassCode = ClassCodeHelper.Normalize(classCode).ToLower();
                var seriesList = await _context.Vocabularies
                    .AsNoTracking()
                    .Where(v => (v.ClassCode != null && v.ClassCode.ToLower() == lowerClassCode) || v.ClassCode == null)
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
                var lowerClassCode = ClassCodeHelper.Normalize(classCode).ToLower();
                var vocabList = await _context.Vocabularies
                    .AsNoTracking()
                    .Where(v => ((v.ClassCode != null && v.ClassCode.ToLower() == lowerClassCode) || v.ClassCode == null) && v.SeriesName == seriesName)
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

                // Chuẩn hoá mã lớp; nếu có mã thì lớp đó phải tồn tại (tránh sinh mã lớp mồ côi)
                var classCode = ClassCodeHelper.Normalize(vocab.ClassCode);
                if (classCode.Length > 0)
                {
                    var targetClass = await ClassCodeHelper.FindClassAsync(_context, classCode);
                    if (targetClass == null)
                    {
                        return BadRequest(new { message = $"Mã lớp '{classCode}' không tồn tại. Vui lòng kiểm tra lại." });
                    }
                    vocab.ClassCode = targetClass.ClassCode;
                }
                else
                {
                    vocab.ClassCode = null;
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
                var normalizedClassCode = ClassCodeHelper.Normalize(classCode);
                if (normalizedClassCode.Length > 0)
                {
                    var targetClass = await ClassCodeHelper.FindClassAsync(_context, normalizedClassCode);
                    if (targetClass == null)
                    {
                        return BadRequest(new { message = $"Mã lớp '{normalizedClassCode}' không tồn tại. Vui lòng kiểm tra lại." });
                    }
                    normalizedClassCode = targetClass.ClassCode;
                }

                using var stream = file.OpenReadStream();
                var document = new XWPFDocument(stream);

                var vocabList = VocabularyDocumentParser.Parse(document, normalizedClassCode, seriesName);

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
                    .AsNoTracking()
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