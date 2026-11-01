using LyraSAT.Models;
using NPOI.XWPF.UserModel;

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
                    var htmlTable = new System.Text.StringBuilder();
                    htmlTable.Append("<div class='passage-table-wrapper'><table class='passage-table'><tbody>");

                    foreach (var row in table.Rows)
                    {
                        htmlTable.Append("<tr>");
                        foreach (var cell in row.GetTableCells())
                        {
                            string cellText = System.Net.WebUtility.HtmlEncode(cell.GetText());
                            htmlTable.Append($"<td>{cellText}</td>");
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
                if (string.IsNullOrEmpty(line))
                {
                    if (currentQ != null)
                    {
                        if (isReadingPassage) passageLines.Add(rawLine);
                        else questionLines.Add(rawLine);
                    }
                    continue;
                }

                if (line.StartsWith("Text:", StringComparison.OrdinalIgnoreCase))
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

                    string contentAfterColon = line.Substring(5).TrimStart();
                    if (!string.IsNullOrEmpty(contentAfterColon))
                    {
                        passageLines.Add(contentAfterColon);
                    }
                }
                else if (line.StartsWith("Question:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Câu:", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentQ == null)
                    {
                        currentQ = new Question { TestId = testId };
                    }

                    isReadingPassage = false;

                    string contentAfterColon = line.Substring(line.IndexOf(':') + 1).TrimStart();
                    if (!string.IsNullOrEmpty(contentAfterColon))
                    {
                        questionLines.Add(contentAfterColon);
                    }
                }
                else if (currentQ != null)
                {
                    if (line.Contains("A:") && line.Contains("B:") && line.Contains("C:") && line.Contains("D:"))
                    {
                        ExtractAllOptionsInline(currentQ, line);
                    }
                    else if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^[A-D][:\.]"))
                    {
                        char optKey = char.ToUpper(line[0]);
                        string optVal = line.Substring(2).TrimStart();

                        if (optKey == 'A') currentQ.OptionA = optVal;
                        else if (optKey == 'B') currentQ.OptionB = optVal;
                        else if (optKey == 'C') currentQ.OptionC = optVal;
                        else if (optKey == 'D') currentQ.OptionD = optVal;
                    }
                    else if (line.StartsWith("ANSWER", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Đáp án", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(new[] { ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 1)
                        {
                            currentQ.CorrectAnswer = parts.Last().Trim().ToUpper();
                        }
                    }
                    else if (line.StartsWith("VIDEO", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Link video", StringComparison.OrdinalIgnoreCase))
                    {
                        var colonIdx = line.IndexOf(':');
                        if (colonIdx >= 0 && colonIdx < line.Length - 1)
                        {
                            currentQ.VideoExplanationUrl = line.Substring(colonIdx + 1).Trim();
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
            var paragraphText = paragraph.ParagraphText;
            if (paragraphText == null) return;

            var normalizedText = paragraphText.Replace("\r\n", "\n").Replace('\r', '\n');
            var prefix = string.IsNullOrEmpty(paragraph.GetNumID()) ? string.Empty : "• ";
            var paragraphLines = normalizedText.Split('\n', StringSplitOptions.None);

            for (var index = 0; index < paragraphLines.Length; index++)
            {
                if (index == 0 && !string.IsNullOrWhiteSpace(paragraphLines[index]))
                {
                    lines.Add(prefix + paragraphLines[index]);
                }
                else
                {
                    lines.Add(paragraphLines[index]);
                }
            }
        }

        private static string JoinPreservingLineBreaks(List<string> lines)
        {
            return string.Join("\n", lines).Trim('\r', '\n');
        }

        private static void ExtractAllOptionsInline(Question q, string text)
        {
            try
            {
                var parts = System.Text.RegularExpressions.Regex.Split(text, @"(?=[A-D][:\.])");
                foreach (var p in parts)
                {
                    string trimmed = p.TrimStart();
                    if (trimmed.StartsWith("A:") || trimmed.StartsWith("A.")) q.OptionA = trimmed.Substring(2).Trim();
                    else if (trimmed.StartsWith("B:") || trimmed.StartsWith("B.")) q.OptionB = trimmed.Substring(2).Trim();
                    else if (trimmed.StartsWith("C:") || trimmed.StartsWith("C.")) q.OptionC = trimmed.Substring(2).Trim();
                    else if (trimmed.StartsWith("D:") || trimmed.StartsWith("D.")) q.OptionD = trimmed.Substring(2).Trim();
                }
            }
            catch { }
        }
    }
}
