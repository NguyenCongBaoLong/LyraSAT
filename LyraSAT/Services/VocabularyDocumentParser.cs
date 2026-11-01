using LyraSAT.Models;
using NPOI.XWPF.UserModel;

namespace LyraSAT.Services
{
    /// <summary>
    /// Bóc tách file Word (.docx) bộ từ vựng thành danh sách Vocabulary.
    /// </summary>
    public static class VocabularyDocumentParser
    {
        public static List<Vocabulary> Parse(XWPFDocument document, string classCode, string seriesName)
        {
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

            return vocabList;
        }
    }
}
