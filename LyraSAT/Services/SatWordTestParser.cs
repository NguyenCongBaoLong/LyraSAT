using LyraSAT.Models;
using NPOI.XWPF.UserModel;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LyraSAT.Services
{
    /// <summary>
    /// Bóc tách file Word (.docx) đề SAT thành danh sách Question.
    /// Toàn bộ quy tắc nhận dạng giữ nguyên như bản gốc trong TestsController.
    /// </summary>
    public static class SatWordTestParser
    {
        public static List<string> ExtractDocumentLines(XWPFDocument document)
        {
            var extractedLines = new List<string>();

            foreach (var element in document.BodyElements)
            {
                if (element is XWPFParagraph paragraph)
                {
                    AddParagraphLines(extractedLines, paragraph);
                }
                else if (element is XWPFTable table)
                {
                    // Chuyển đổi bảng Word thành chuỗi HTML chuẩn
                    var htmlTable = new StringBuilder();
                    htmlTable.Append("<div class='rich-table-wrapper'><table class='rich-table'><tbody>");

                    foreach (var row in table.Rows)
                    {
                        htmlTable.Append("<tr>");
                        foreach (var cell in row.GetTableCells())
                        {
                            var cellHtml = string.Join("<br>", cell.Paragraphs
                                .Select(RenderParagraph)
                                .Where(value => !string.IsNullOrWhiteSpace(value)));
                            htmlTable.Append($"<td>{cellHtml}</td>");
                        }
                        htmlTable.Append("</tr>");
                    }

                    htmlTable.Append("</tbody></table></div>");
                    extractedLines.Add(htmlTable.ToString());
                }
            }

            return extractedLines;
        }

        public static List<Question> Parse(List<string> lines, int testId)
        {
            var questions = new List<Question>();
            Question? currentQ = null;
            var passageLines = new List<string>();
            var questionLines = new List<string>();
            bool isReadingPassage = true;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                string plainLine = StripHtml(line).Trim();
                if (string.IsNullOrEmpty(plainLine))
                {
                    if (currentQ != null)
                    {
                        if (isReadingPassage) passageLines.Add(rawLine);
                        else questionLines.Add(rawLine);
                    }
                    continue;
                }

                if (plainLine.StartsWith("Text:", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentQ != null)
                    {
                        currentQ.PassageText = JoinPreservingLineBreaks(passageLines);
                        currentQ.QuestionText = JoinPreservingLineBreaks(questionLines);

                        // Kiểm tra an toàn: Bắt buộc phải có QuestionText và OptionA để tránh lỗi cột NOT NULL của DB
                        if (!string.IsNullOrEmpty(currentQ.QuestionText) && !string.IsNullOrEmpty(currentQ.OptionA))
                        {
                            questions.Add(currentQ);
                        }
                    }

                    currentQ = new Question { TestId = testId };
                    passageLines.Clear();
                    questionLines.Clear();
                    isReadingPassage = true;

                    string contentAfterColon = RemovePrefix(line, "Text:");
                    if (!string.IsNullOrEmpty(contentAfterColon))
                    {
                        passageLines.Add(contentAfterColon);
                    }
                }
                else if (plainLine.StartsWith("Question:", StringComparison.OrdinalIgnoreCase) || plainLine.StartsWith("Câu:", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentQ == null)
                    {
                        currentQ = new Question { TestId = testId };
                    }

                    isReadingPassage = false;

                    string contentAfterColon = RemovePrefix(line, plainLine[..(plainLine.IndexOf(':') + 1)]);
                    if (!string.IsNullOrEmpty(contentAfterColon))
                    {
                        questionLines.Add(contentAfterColon);
                    }
                }
                else if (currentQ != null)
                {
                    if (plainLine.Contains("A:") && plainLine.Contains("B:") && plainLine.Contains("C:") && plainLine.Contains("D:"))
                    {
                        ExtractAllOptionsInline(currentQ, line);
                    }
                    else if (Regex.IsMatch(plainLine, @"^[A-D][:\.]"))
                    {
                        char optKey = char.ToUpper(plainLine[0]);
                        string optVal = RemovePrefix(line, plainLine[..2]);

                        if (optKey == 'A') currentQ.OptionA = optVal;
                        else if (optKey == 'B') currentQ.OptionB = optVal;
                        else if (optKey == 'C') currentQ.OptionC = optVal;
                        else if (optKey == 'D') currentQ.OptionD = optVal;
                    }
                    else if (plainLine.StartsWith("ANSWER", StringComparison.OrdinalIgnoreCase) || plainLine.StartsWith("Đáp án", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = plainLine.Split(new[] { ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 1)
                        {
                            currentQ.CorrectAnswer = parts.Last().Trim().ToUpper();
                        }
                    }
                    else if (plainLine.StartsWith("VIDEO", StringComparison.OrdinalIgnoreCase) || plainLine.StartsWith("Link video", StringComparison.OrdinalIgnoreCase))
                    {
                        var colonIdx = plainLine.IndexOf(':');
                        if (colonIdx >= 0 && colonIdx < plainLine.Length - 1)
                        {
                            currentQ.VideoExplanationUrl = plainLine[(colonIdx + 1)..].Trim();
                        }
                    }
                    else
                    {
                        if (isReadingPassage)
                        {
                            passageLines.Add(line);
                        }
                        else
                        {
                            questionLines.Add(line);
                        }
                    }
                }
                else
                {
                    passageLines.Add(line);
                }
            }

            if (currentQ != null)
            {
                currentQ.PassageText = JoinPreservingLineBreaks(passageLines);
                currentQ.QuestionText = JoinPreservingLineBreaks(questionLines);

                if (!string.IsNullOrEmpty(currentQ.QuestionText) && !string.IsNullOrEmpty(currentQ.OptionA))
                {
                    questions.Add(currentQ);
                }
            }

            return questions;
        }

        private static void AddParagraphLines(List<string> lines, XWPFParagraph paragraph)
        {
            var html = RenderParagraph(paragraph);
            if (string.IsNullOrWhiteSpace(html)) return;

            var prefix = string.IsNullOrEmpty(paragraph.GetNumID()) ? string.Empty : "• ";
            lines.Add(prefix + html);
        }

        private static string JoinPreservingLineBreaks(List<string> lines)
        {
            return string.Join("<br>", lines.Where(line => !string.IsNullOrWhiteSpace(line))).Trim();
        }

        private static void ExtractAllOptionsInline(Question q, string text)
        {
            try
            {
                var parts = Regex.Split(text, @"(?=[A-D][:\.])");
                foreach (var p in parts)
                {
                    string trimmed = p.TrimStart();
                    string plain = StripHtml(trimmed);
                    if (plain.StartsWith("A:") || plain.StartsWith("A.")) q.OptionA = RemovePrefix(trimmed, plain[..2]);
                    else if (plain.StartsWith("B:") || plain.StartsWith("B.")) q.OptionB = RemovePrefix(trimmed, plain[..2]);
                    else if (plain.StartsWith("C:") || plain.StartsWith("C.")) q.OptionC = RemovePrefix(trimmed, plain[..2]);
                    else if (plain.StartsWith("D:") || plain.StartsWith("D.")) q.OptionD = RemovePrefix(trimmed, plain[..2]);
                }
            }
            catch (ArgumentException) { }
        }

        private static string RenderParagraph(XWPFParagraph paragraph)
        {
            var content = new StringBuilder();
            foreach (var run in paragraph.Runs)
            {
                AppendEmbeddedPictures(content, run);

                var text = run.ToString();
                if (string.IsNullOrEmpty(text)) continue;

                var encoded = WebUtility.HtmlEncode(text).Replace("\r\n", "<br>").Replace("\n", "<br>");
                if (run.IsBold) encoded = $"<strong>{encoded}</strong>";
                if (run.IsItalic) encoded = $"<em>{encoded}</em>";
                if (HasUnderline(run)) encoded = $"<u>{encoded}</u>";
                content.Append(encoded);
            }

            return content.ToString();
        }

        private static void AppendEmbeddedPictures(StringBuilder content, XWPFRun run)
        {
            foreach (var picture in run.GetEmbeddedPictures())
            {
                var pictureData = picture.GetPictureData();
                var bytes = pictureData?.Data;
                if (bytes == null || bytes.Length == 0) continue;

                var fileName = pictureData.FileName;
                var mimeType = GetImageMimeType(fileName);
                var dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
                content.Append($"<img src=\"{dataUri}\" alt=\"Hình minh họa\" />");
            }
        }

        private static string GetImageMimeType(string? fileName)
        {
            return Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".webp" => "image/webp",
                _ => "image/png"
            };
        }

        private static bool HasUnderline(XWPFRun run)
        {
            var runProperties = run.GetCTR()
                .GetType()
                .GetProperty("rPr")?
                .GetValue(run.GetCTR());

            return runProperties?
                .GetType()
                .GetProperty("u")?
                .GetValue(runProperties) != null;
        }

        private static string StripHtml(string value)
        {
            var withLineBreaks = Regex.Replace(value, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
            return WebUtility.HtmlDecode(Regex.Replace(withLineBreaks, "<[^>]+>", string.Empty));
        }

        private static string RemovePrefix(string html, string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return html.Trim();
            var index = html.IndexOf(':');
            return index >= 0 ? html[(index + 1)..].TrimStart() : html.Trim();
        }
    }
}
