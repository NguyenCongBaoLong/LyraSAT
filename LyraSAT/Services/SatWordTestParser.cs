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

        /// <summary>
        /// Dòng mở đầu câu hỏi theo đúng mẫu đề của trung tâm: "Question 1." / "Question 12:" / "Câu 3."
        /// (có số thứ tự, dấu chấm hoặc hai chấm) và cả dạng cũ "Question:" không số.
        /// </summary>
        private static readonly Regex QuestionHeading = new(
            @"^\s*(?:Question|Câu|Cau)\s*(?:(\d+)\s*[.:)]?|:)\s*(.*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
                    // Bảng 2x2 hoặc 1x4 mà mỗi ô là một đáp án (thường gặp ở đề Toán):
                    // tách thành 4 dòng "A. ...", "B. ..." để phần đọc câu hỏi gắn đúng vào đáp án.
                    var optionCells = ReadOptionGrid(table);
                    if (optionCells != null)
                    {
                        // Mỗi đáp án thành 2 dòng: nhãn "A." rồi nội dung (bảng/chữ) để phần
                        // đọc câu hỏi gắn đúng nội dung, kể cả bảng HTML.
                        for (int i = 0; i < 4; i++)
                        {
                            extractedLines.Add($"{(char)('A' + i)}.");
                            extractedLines.Add(optionCells[i]);
                        }
                    }
                    else
                    {
                        extractedLines.Add(RenderTable(table));
                    }
                }
            }

            return extractedLines;
        }

        /// <summary>
        /// Bảng Word thành HTML, giữ cả bảng lồng trong ô (đề Toán hay có bảng trong đáp án).
        /// </summary>
        private static string RenderTable(XWPFTable table)
        {
            var html = new StringBuilder("<div class='rich-table-wrapper'><table class='rich-table'><tbody>");

            foreach (var row in table.Rows)
            {
                html.Append("<tr>");
                foreach (var cell in row.GetTableCells())
                {
                    html.Append("<td>").Append(RenderCell(cell)).Append("</td>");
                }
                html.Append("</tr>");
            }

            html.Append("</tbody></table></div>");
            return html.ToString();
        }

        private static string RenderCell(XWPFTableCell cell)
        {
            var parts = new List<string>();

            foreach (var element in cell.BodyElements)
            {
                if (element is XWPFParagraph paragraph)
                {
                    var html = RenderParagraph(paragraph);
                    if (!string.IsNullOrWhiteSpace(html)) parts.Add(html);
                }
                else if (element is XWPFTable nested)
                {
                    parts.Add(RenderTable(nested));
                }
            }

            return string.Join("<br>", parts);
        }

        /// <summary>
        /// Bảng 2x2 (hoặc 1x4) mà mỗi ô là một đáp án. Chỉ nhận khi mỗi ô có nhãn A/B/C/D
        /// hoặc chứa bảng riêng, để không nhầm bảng số liệu 2x2 của đề bài thành đáp án.
        /// </summary>
        private static List<string>? ReadOptionGrid(XWPFTable table)
        {
            var cells = table.Rows.SelectMany(row => row.GetTableCells()).ToList();
            if (cells.Count != 4) return null;

            var contents = new List<string>();
            bool hasLabelledCell = false;
            bool hasNestedTable = false;

            foreach (var cell in cells)
            {
                var html = RenderCell(cell).Trim();
                if (html.Length == 0) return null;

                var labelMatch = OptionLabel.Match(html);
                if (labelMatch.Success)
                {
                    hasLabelledCell = true;
                    html = NormalizeOptionText(html.Substring(labelMatch.Length), char.ToUpperInvariant(labelMatch.Groups[1].Value[0]));
                }

                if (html.Contains("<table", StringComparison.OrdinalIgnoreCase)) hasNestedTable = true;
                contents.Add(html);
            }

            if (!hasLabelledCell && !hasNestedTable) return null;

            return contents;
        }

        public static List<Question> Parse(List<string> lines, int testId)
        {
            var questions = new List<Question>();
            Question? currentQ = null;
            var passageLines = new List<string>();
            var questionLines = new List<string>();
            bool isReadingPassage = true;
            char? pendingOption = null;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                string plainLine = StripHtml(line).Trim();
                if (string.IsNullOrEmpty(plainLine))
                {
                    // Dòng chỉ có ảnh (không có chữ) vẫn phải vào đúng đáp án đang mở.
                    if (currentQ != null && pendingOption.HasValue && !string.IsNullOrWhiteSpace(line))
                    {
                        AppendOptionContent(currentQ, pendingOption.Value, line);
                    }
                    else if (currentQ != null && pendingOption == null)
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
                    FinalizeQuestion(questions, currentQ, passageLines, questionLines);
                    currentQ = new Question { TestId = testId };
                    isReadingPassage = true;
                    pendingOption = null;

                    string contentAfterColon = RemovePrefix(line, "Text:");
                    if (!string.IsNullOrEmpty(contentAfterColon))
                    {
                        passageLines.Add(contentAfterColon);
                    }
                }
                else if (TryReadQuestionHeading(plainLine, out string questionHeadingRest))
                {
                    // Câu tự luận thường không có "Text:" ngăn cách, nên gặp dòng mở đầu câu mới
                    // là biết câu trước đã xong; chốt câu trước rồi mở câu mới.
                    if (currentQ != null &&
                        (!string.IsNullOrWhiteSpace(currentQ.QuestionText) ||
                         !string.IsNullOrWhiteSpace(currentQ.CorrectAnswer) ||
                         questionLines.Any(line => !string.IsNullOrWhiteSpace(line))))
                    {
                        FinalizeQuestion(questions, currentQ, passageLines, questionLines);
                        currentQ = null;
                    }

                    if (currentQ == null)
                    {
                        currentQ = new Question { TestId = testId };
                    }

                    isReadingPassage = false;
                    pendingOption = null;

                    if (!string.IsNullOrEmpty(questionHeadingRest))
                    {
                        questionLines.Add(questionHeadingRest);
                    }
                }
                else if (currentQ != null)
                {
                    if (IsAnswerLine(probe))
                    {
                        pendingOption = null;
                        string? answer = ExtractAnswerLetter(probe);
                        bool hasOptions = !string.IsNullOrWhiteSpace(currentQ.OptionA)
                            || !string.IsNullOrWhiteSpace(currentQ.OptionB)
                            || !string.IsNullOrWhiteSpace(currentQ.OptionC)
                            || !string.IsNullOrWhiteSpace(currentQ.OptionD);

                        if (answer != null)
                        {
                            currentQ.CorrectAnswer = answer;
                            currentQ.QuestionType = AnswerMatcher.MultipleChoice;
                        }
                        else if (!hasOptions)
                        {
                            // Không có A/B/C/D và đáp án không phải chữ cái => câu tự luận.
                            var accepted = ExtractFreeResponseAnswers(probe);
                            if (accepted.Count > 0)
                            {
                                currentQ.QuestionType = AnswerMatcher.FreeResponse;
                                currentQ.AcceptedAnswers = string.Join(" | ", accepted);
                                currentQ.CorrectAnswer = accepted[0];
                            }
                        }
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
                        pendingOption = null;
                    }
                    else if (OptionLabel.IsMatch(probe))
                    {
                        var match = OptionLabel.Match(probe);
                        char optKey = char.ToUpperInvariant(match.Groups[1].Value[0]);
                        AssignOption(currentQ, optKey, NormalizeOptionText(probe.Substring(match.Length), optKey));
                        pendingOption = optKey;
                    }
                    else if (pendingOption.HasValue)
                    {
                        // Dòng/bảng/hình nằm ngay sau nhãn đáp án (bảng trong đáp án, phân số
                        // xếp dọc kiểu A. / 1 / 400) thì thuộc về đáp án đó.
                        AppendOptionContent(currentQ, pendingOption.Value, line);
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

            FinalizeQuestion(questions, currentQ, passageLines, questionLines);

            return questions;
        }

        /// <summary>
        /// Chốt câu hỏi đang đọc: gắn đoạn văn + nội dung câu, thêm vào danh sách nếu dùng được,
        /// rồi xoá buffer để đọc câu tiếp theo.
        /// </summary>
        private static void FinalizeQuestion(List<Question> questions, Question? current, List<string> passageLines, List<string> questionLines)
        {
            if (current == null) return;

            // Đề Toán thường không có câu hỏi tách riêng: cả khối đề bài (có công thức, bảng)
            // chính là câu hỏi. Nếu chưa có dòng Question: nào mà đã có đáp án/lựa chọn thì
            // lấy toàn bộ phần đã đọc làm nội dung câu hỏi, không coi là đoạn văn.
            bool hasAnswerOrOptions =
                !string.IsNullOrWhiteSpace(current.CorrectAnswer) ||
                !string.IsNullOrWhiteSpace(current.OptionA) ||
                !string.IsNullOrWhiteSpace(current.OptionB) ||
                !string.IsNullOrWhiteSpace(current.OptionC) ||
                !string.IsNullOrWhiteSpace(current.OptionD);

            if (hasAnswerOrOptions &&
                !questionLines.Any(line => !string.IsNullOrWhiteSpace(line)) &&
                passageLines.Any(line => !string.IsNullOrWhiteSpace(line)))
            {
                questionLines.AddRange(passageLines);
                passageLines.Clear();
            }

            current.PassageText = JoinPreservingLineBreaks(passageLines);
            current.QuestionText = JoinPreservingLineBreaks(questionLines);

            // Cột Options trong DB là NOT NULL: câu tự luận để chuỗi rỗng chứ không được null.
            current.OptionA ??= string.Empty;
            current.OptionB ??= string.Empty;
            current.OptionC ??= string.Empty;
            current.OptionD ??= string.Empty;
            current.CorrectAnswer ??= string.Empty;
            current.QuestionType = string.IsNullOrWhiteSpace(current.QuestionType)
                ? AnswerMatcher.MultipleChoice
                : current.QuestionType;

            if (IsUsableQuestion(current))
            {
                questions.Add(current);
            }

            passageLines.Clear();
            questionLines.Clear();
        }

        /// <summary>
        /// Câu chỉ được lưu khi có nội dung câu hỏi. Câu tự luận không cần lựa chọn;
        /// câu trắc nghiệm bắt buộc có OptionA vì cột này NOT NULL trong DB.
        /// </summary>
        private static bool IsUsableQuestion(Question q)
        {
            if (string.IsNullOrWhiteSpace(q.QuestionText)) return false;
            if (AnswerMatcher.IsFreeResponse(q.QuestionType)) return true;
            return !string.IsNullOrWhiteSpace(q.OptionA);
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

                if (AnswerMatcher.IsFreeResponse(q.QuestionType))
                {
                    ValidateFreeResponse(q, number, options, issues);
                    continue;
                }

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
        /// Câu tự luận: không có A/B/C/D, đáp án là danh sách cách ghi đúng cách nhau bằng dấu |,
        /// mỗi cách ghi phải đọc được thành số (phân số, thập phân, số âm...).
        /// </summary>
        private static void ValidateFreeResponse(Question q, int number, string[] options, List<string> issues)
        {
            if (options.Any(text => !string.IsNullOrWhiteSpace(text)))
            {
                issues.Add($"Câu {number}: câu tự luận không được có đáp án A/B/C/D.");
            }

            var accepted = AnswerMatcher.SplitAccepted(q.AcceptedAnswers);
            if (accepted.Count == 0)
            {
                issues.Add($"Câu {number}: không tìm thấy đáp án tự luận (cần dòng ANSWER: 3/2 | 1.5).");
                return;
            }

            var bad = accepted.Where(answer => !AnswerMatcher.TryParseNumber(answer, out _)).ToList();
            if (bad.Count > 0)
            {
                issues.Add($"Câu {number}: đáp án tự luận {string.Join(", ", bad.Select(x => $"'{x}'"))} không đọc được thành số.");
            }

            var stored = (q.AcceptedAnswers ?? "").Trim();
            if (stored.EndsWith("|") || stored.EndsWith(";") || stored.Contains("||"))
            {
                issues.Add($"Câu {number}: danh sách đáp án tự luận bị thừa dấu phân tách.");
            }
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
        /// Nhận dòng mở đầu câu hỏi. Chấp nhận cả mẫu của trung tâm ("Question 1.", "Question 12:")
        /// lẫn mẫu cũ ("Question:", "Câu 1: ..."). Trả về phần nội dung nằm cùng dòng nếu có.
        /// </summary>
        private static bool TryReadQuestionHeading(string plainLine, out string rest)
        {
            rest = string.Empty;

            // "Questioning the premise..." không phải tiêu đề câu hỏi.
            var match = QuestionHeading.Match(plainLine);
            if (!match.Success) return false;

            rest = match.Groups[2].Value.Trim();
            return true;
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
        /// Đọc đáp án tự luận trong dòng ANSWER / Đáp án: "ANSWER: 3/2 | 1.5 | 1,5".
        /// Dấu | (hoặc ;) phân tách các cách ghi đều được tính đúng. Bỏ dấu chấm/dấu phẩy
        /// cuối câu nếu phần còn lại đọc được thành số.
        /// </summary>
        private static List<string> ExtractFreeResponseAnswers(string probe)
        {
            var colonIdx = probe.IndexOf(':');
            var tail = colonIdx >= 0 ? probe.Substring(colonIdx + 1) : probe;

            var accepted = new List<string>();
            foreach (var rawPart in tail.Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var part = rawPart.Trim().TrimEnd('.', '。', ',').Trim();
                if (part.Length == 0) continue;
                accepted.Add(part);
            }

            return accepted;
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

        private static string GetOption(Question q, char key) => key switch
        {
            'A' => q.OptionA ?? string.Empty,
            'B' => q.OptionB ?? string.Empty,
            'C' => q.OptionC ?? string.Empty,
            'D' => q.OptionD ?? string.Empty,
            _ => string.Empty
        };

        /// <summary>
        /// Nối thêm nội dung vào một đáp án: bảng/hình thì dán liền, phân số xếp dọc
        /// ("1" rồi "400") thì nối bằng dấu /, chữ thường thì xuống dòng.
        /// </summary>
        private static void AppendOptionContent(Question q, char key, string raw)
        {
            var addition = (raw ?? string.Empty).Trim();
            if (addition.Length == 0) return;

            var existing = GetOption(q, key).Trim();
            if (existing.Length == 0)
            {
                AssignOption(q, key, addition);
                return;
            }

            string separator;
            if (addition.StartsWith("<", StringComparison.Ordinal))
            {
                separator = string.Empty;
            }
            else if (AnswerMatcher.TryParseNumber(existing, out _) && AnswerMatcher.TryParseNumber(addition, out _))
            {
                separator = "/";
            }
            else
            {
                separator = "<br>";
            }

            AssignOption(q, key, existing + separator + addition);
        }

        private static void AddParagraphLines(List<string> lines, XWPFParagraph paragraph)
        {
            var html = RenderParagraph(paragraph);
            if (string.IsNullOrWhiteSpace(html))
            {
                // Đoạn trống trong Word chính là dòng cách: giữ lại để web không dồn chữ vào nhau.
                lines.Add(string.Empty);
                return;
            }

            var prefix = string.IsNullOrEmpty(paragraph.GetNumID()) ? string.Empty : "• ";
            lines.Add(prefix + html);
        }

        /// <summary>
        /// Nối các dòng của một khối văn bản, giữ cả dòng trống (tối đa một dòng trống liên tiếp)
        /// để khoảng cách giữa các đoạn trong file Word còn nguyên khi hiển thị trên web.
        /// </summary>
        private static string JoinPreservingLineBreaks(List<string> lines)
        {
            var content = new StringBuilder();

            foreach (var rawLine in lines)
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    if (content.Length > 0) content.Append("<br>");
                    continue;
                }

                if (content.Length > 0) content.Append("<br>");
                content.Append(rawLine.Trim());
            }

            var html = content.ToString().Trim();
            while (html.EndsWith("<br>", StringComparison.OrdinalIgnoreCase))
            {
                html = html[..^4].TrimEnd();
            }

            return html;
        }

        private static string RenderParagraph(XWPFParagraph paragraph)
        {
            var content = new StringBuilder();
            foreach (var run in paragraph.Runs)
            {
                AppendEmbeddedPictures(content, run);

                var text = run.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    var encoded = WebUtility.HtmlEncode(text).Replace("\r\n", "<br>").Replace("\n", "<br>");
                    if (run.IsBold) encoded = $"<strong>{encoded}</strong>";
                    if (run.IsItalic) encoded = $"<em>{encoded}</em>";
                    if (HasUnderline(run)) encoded = $"<u>{encoded}</u>";
                    content.Append(encoded);
                }

                // Shift+Enter trong Word tạo thẻ <w:br/> nằm trong run, NPOI không trả về như
                // ký tự nên trước đây các dòng bị dính liền. Thêm lại đúng số lần ngắt dòng.
                content.Append(ReadLineBreaks(run));
            }

            return content.ToString();
        }

        private static string ReadLineBreaks(XWPFRun run)
        {
            var ctr = run.GetCTR();
            if (ctr == null) return string.Empty;

            int count = ctr.SizeOfBrArray() + ctr.SizeOfCrArray();
            return count <= 0 ? string.Empty : string.Concat(Enumerable.Repeat("<br>", count));
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
