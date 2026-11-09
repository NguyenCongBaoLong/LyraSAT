using LyraSAT.Models;
using NPOI.XWPF.UserModel;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LyraSAT.Services
{
    /// <summary>
    /// Bóc tách file Word (.docx) đề SAT thành danh sách Question.
    /// Quy tắc nhận dạng giữ nguyên bản gốc, chỉ gia cố phần đáp án A/B/C/D vì đây là
    /// chỗ hay sinh lỗi dữ liệu: nhãn chữ cái bị lặp lại, đáp án bị dính vào nhau trên
    /// một dòng, hoặc bị nuốt vào đáp án A khi cả bốn lựa chọn nằm chung một đoạn.
    /// </summary>
    public static class SatWordTestParser
    {
        /// <summary>Nhãn đáp án ở đầu dòng: "A.", "A:", "A)" (không phân biệt hoa thường).</summary>
        private static readonly Regex OptionLabel = new(
            @"^\s*([A-Da-d])\s*[:.)]\s*", RegexOptions.Compiled);

        /// <summary>Ký hiệu bullet do Word tự sinh khi đoạn văn là danh sách.</summary>
        private static readonly Regex BulletPrefix = new(
            @"^[\s\u00A0]*[•·▪◦‣∙\-–—\*]+\s*", RegexOptions.Compiled);

        /// <summary>Nhãn đáp án nằm giữa dòng, dùng để tách khi cả bốn đáp án chung một dòng.</summary>
        private static readonly Regex InlineLabel = new(
            @"(?<![A-Za-z0-9])[A-Da-d]\s*[:.)]\s*", RegexOptions.Compiled);

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

                // Dòng do Word tự đánh bullet thì bỏ bullet khi nhận dạng,
                // nhưng vẫn giữ nguyên văn cho phần đoạn đọc để không mất bullet.
                string probe = StripBullet(plainLine);

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
                    if (IsAnswerLine(probe))
                    {
                        string? answer = ExtractAnswerLetter(probe);
                        if (answer != null) currentQ.CorrectAnswer = answer;
                    }
                    else if (probe.StartsWith("VIDEO", StringComparison.OrdinalIgnoreCase) || probe.StartsWith("Link video", StringComparison.OrdinalIgnoreCase))
                    {
                        var colonIdx = probe.IndexOf(':');
                        if (colonIdx >= 0 && colonIdx < probe.Length - 1)
                        {
                            currentQ.VideoExplanationUrl = probe.Substring(colonIdx + 1).Trim();
                        }
                    }
                    else if (TryExtractInlineOptions(currentQ, probe))
                    {
                        // Bốn đáp án nằm chung một dòng: đã tách xong.
                    }
                    else if (OptionLabel.IsMatch(probe))
                    {
                        var match = OptionLabel.Match(probe);
                        char optKey = char.ToUpperInvariant(match.Groups[1].Value[0]);
                        AssignOption(currentQ, optKey, NormalizeOptionText(probe.Substring(match.Length), optKey));
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

        /// <summary>
        /// Soát lại toàn bộ câu hỏi vừa bóc tách. Trả về danh sách lỗi đọc được bằng tiếng Việt;
        /// rỗng nghĩa là dữ liệu đáp án sạch. Mục đích: không lưu vào DB những câu mà đáp án
        /// bị thiếu, bị trùng, hoặc còn dính nhãn chữ cái.
        /// </summary>
        public static List<string> Validate(IReadOnlyList<Question> questions)
        {
            var issues = new List<string>();
            string[] letters = { "A", "B", "C", "D" };

            for (int i = 0; i < questions.Count; i++)
            {
                var q = questions[i];
                var options = new[] { q.OptionA ?? "", q.OptionB ?? "", q.OptionC ?? "", q.OptionD ?? "" };
                int number = i + 1;

                var missing = new List<string>();
                for (int k = 0; k < 4; k++)
                {
                    if (string.IsNullOrWhiteSpace(options[k])) missing.Add(letters[k]);
                }
                if (missing.Count > 0)
                {
                    issues.Add($"Câu {number}: thiếu nội dung đáp án {string.Join(", ", missing)}.");
                }

                var labelled = new List<string>();
                for (int k = 0; k < 4; k++)
                {
                    var labelMatch = OptionLabel.Match(options[k]);
                    if (labelMatch.Success &&
                        char.ToUpperInvariant(labelMatch.Groups[1].Value[0]) == letters[k][0])
                    {
                        labelled.Add(letters[k]);
                    }
                }
                if (labelled.Count > 0)
                {
                    issues.Add($"Câu {number}: đáp án {string.Join(", ", labelled)} vẫn còn dính nhãn chữ cái, hệ thống không đọc được.");
                }

                var duplicated = options
                    .Select((text, index) => new { text = text.Trim(), letter = letters[index] })
                    .Where(x => x.text.Length > 0)
                    .GroupBy(x => x.text, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => string.Join("=", g.Select(x => x.letter)))
                    .ToList();
                if (duplicated.Count > 0)
                {
                    issues.Add($"Câu {number}: các đáp án {string.Join(", ", duplicated)} trùng nội dung.");
                }

                var answer = (q.CorrectAnswer ?? "").Trim().ToUpperInvariant();
                if (answer.Length == 0)
                {
                    issues.Add($"Câu {number}: không tìm thấy dòng đáp án đúng (ANSWER / Đáp án).");
                }
                else if (answer.Length != 1 || answer[0] < 'A' || answer[0] > 'D')
                {
                    issues.Add($"Câu {number}: đáp án đúng phải là A, B, C hoặc D (đang là '{q.CorrectAnswer}').");
                }
            }

            return issues;
        }

        /// <summary>
        /// Bỏ nhãn chữ cái bị lặp ở đầu đáp án, chỉ khi nhãn đó trùng đúng chữ cái của ô
        /// ("A. A. throughput" -> "throughput"), và bỏ bullet do Word tự sinh.
        /// Không bỏ chữ cái khác vì đáp án có thể mở đầu bằng tên riêng như "D. H. Lawrence".
        /// </summary>
        private static string NormalizeOptionText(string raw, char expected)
        {
            var text = (raw ?? string.Empty).Trim();
            var label = new Regex($@"^{expected}\s*[:.)]\s*", RegexOptions.IgnoreCase);

            for (int guard = 0; guard < 4; guard++)
            {
                var before = text;
                text = BulletPrefix.Replace(text, string.Empty).Trim();

                var match = label.Match(text);
                if (match.Success && match.Length < text.Length)
                {
                    text = text.Substring(match.Length).Trim();
                }

                if (string.Equals(before, text, StringComparison.Ordinal)) break;
            }

            return text;
        }

        private static string StripBullet(string line)
        {
            var stripped = BulletPrefix.Replace(line, string.Empty).Trim();
            return stripped.Length == 0 ? line : stripped;
        }

        private static bool IsAnswerLine(string probe)
        {
            return probe.StartsWith("ANSWER", StringComparison.OrdinalIgnoreCase)
                || probe.StartsWith("Answer", StringComparison.OrdinalIgnoreCase)
                || probe.StartsWith("Đáp án", StringComparison.OrdinalIgnoreCase)
                || probe.StartsWith("Dap an", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Lấy chữ cái đáp án đúng, chịu được "ANSWER D", "ANSWER: D", "Đáp án: d.".
        /// </summary>
        private static string? ExtractAnswerLetter(string probe)
        {
            var colonIdx = probe.IndexOf(':');
            var tail = colonIdx >= 0 ? probe.Substring(colonIdx + 1) : probe;

            foreach (var token in tail.Split(new[] { ' ', '\t', '.', ',', '-', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var upper = token.Trim().ToUpperInvariant();
                if (upper.Length == 1 && upper[0] >= 'A' && upper[0] <= 'D') return upper;
            }

            return null;
        }

        /// <summary>
        /// Trường hợp cả bốn đáp án bị dồn vào một dòng (Word/PDF đổ thành một đoạn).
        /// Chỉ tách khi tìm đủ A, B, C, D theo đúng thứ tự, mỗi phần đều có nội dung.
        /// </summary>
        private static bool TryExtractInlineOptions(Question q, string line)
        {
            var matches = new List<Match>();

            foreach (Match match in InlineLabel.Matches(line))
            {
                char letter = char.ToUpperInvariant(match.Value.Trim()[0]);

                if (matches.Count == 0)
                {
                    if (letter != 'A') continue;
                }
                else
                {
                    char previous = char.ToUpperInvariant(matches[^1].Value.Trim()[0]);
                    if (letter != previous + 1) continue;
                }

                matches.Add(match);
                if (matches.Count == 4) break;
            }

            if (matches.Count < 4) return false;

            // Thử tách trước, chỉ ghi vào câu hỏi khi cả bốn phần đều có nội dung,
            // tránh làm hỏng câu mà dòng đó thật ra chỉ là câu văn bình thường.
            var values = new string[4];
            for (int i = 0; i < 4; i++)
            {
                int start = matches[i].Index + matches[i].Length;
                int end = i < 3 ? matches[i + 1].Index : line.Length;
                if (end <= start) return false;

                var value = NormalizeOptionText(line.Substring(start, end - start), (char)('A' + i));
                if (value.Length == 0) return false;
                values[i] = value;
            }

            for (int i = 0; i < 4; i++)
            {
                AssignOption(q, (char)('A' + i), values[i]);
            }

            return true;
        }

        private static void AssignOption(Question q, char key, string value)
        {
            switch (key)
            {
                case 'A': q.OptionA = value; break;
                case 'B': q.OptionB = value; break;
                case 'C': q.OptionC = value; break;
                case 'D': q.OptionD = value; break;
            }
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
