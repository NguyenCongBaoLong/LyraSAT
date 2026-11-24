using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LyraSAT.Services
{
    /// <summary>
    /// So khớp đáp án học sinh với đáp án đúng.
    /// Câu trắc nghiệm so sánh chữ cái; câu tự luận (SAT math grid-in) so sánh GIÁ TRỊ SỐ,
    /// nên "3/2", "1.5" và "1,5" đều được tính đúng.
    /// </summary>
    public static class AnswerMatcher
    {
        public const string MultipleChoice = "MultipleChoice";
        public const string FreeResponse = "FreeResponse";

        // Sai số tuyệt đối khi so hai số thực: đủ rộng để chấp nhận làm tròn chuẩn,
        // đủ hẹp để không chấm đúng 2.5 khi học sinh gõ 2.501.
        private const double Tolerance = 5e-4;

        private static readonly Regex UsThousandsRegex = new(
            @"^[+-]?\d{1,3}(,\d{3})+(\.\d+)?$", RegexOptions.Compiled);

        public static bool IsFreeResponse(string? questionType) =>
            string.Equals((questionType ?? "").Trim(), FreeResponse, StringComparison.OrdinalIgnoreCase);

        /// <summary>Các cách ghi được chấp nhận: "3/2 | 1.5 | 1,5" -> ["3/2", "1.5", "1,5"].</summary>
        public static List<string> SplitAccepted(string? acceptedAnswers)
        {
            return (acceptedAnswers ?? string.Empty)
                .Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToList();
        }

        /// <summary>
        /// Chấm một câu. correctAnswer là đáp án hiển thị (chữ cái với trắc nghiệm,
        /// cách ghi đầu tiên với tự luận); acceptedAnswers là danh sách cách ghi đúng.
        /// </summary>
        public static bool IsCorrect(string? correctAnswer, string? acceptedAnswers, string? studentAnswer)
        {
            var student = (studentAnswer ?? string.Empty).Trim();
            if (student.Length == 0) return false;

            var accepted = SplitAccepted(acceptedAnswers);
            if (accepted.Count == 0 && !string.IsNullOrWhiteSpace(correctAnswer))
            {
                accepted.Add(correctAnswer.Trim());
            }
            if (accepted.Count == 0) return false;

            return accepted.Any(answer => MatchesOne(student, answer));
        }

        /// <summary>Đáp án tự luận có đọc được không (dùng khi soát file Word trước khi lưu).</summary>
        public static bool HasAcceptedAnswers(string? acceptedAnswers) => SplitAccepted(acceptedAnswers).Count > 0;

        private static bool MatchesOne(string student, string accepted)
        {
            if (TryParseNumber(student, out var studentNumber) &&
                TryParseNumber(accepted, out var acceptedNumber))
            {
                return NumbersEqual(studentNumber, acceptedNumber, student, accepted);
            }

            return string.Equals(
                NormalizeText(student),
                NormalizeText(accepted),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Chuẩn hoá chuỗi số: bỏ khoảng trắng, $, đổi dấu trừ Unicode và dấu phẩy thập phân/nghìn.</summary>
        public static string NormalizeNumberText(string? raw)
        {
            var text = (raw ?? string.Empty).Trim()
                .Replace("\u2212", "-")   // dấu trừ toán học
                .Replace("\u2013", "-")   // en dash
                .Replace("\u2014", "-")   // em dash
                .Replace("\u2044", "/")   // fraction slash
                .Replace("\u00A0", " ");  // khoảng trắng không ngắt

            var plain = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (!char.IsWhiteSpace(ch)) plain.Append(ch);
            }

            var normalized = plain.ToString();

            // Học sinh có thể gõ "$54" hoặc "-$54"
            if (normalized.StartsWith("-$", StringComparison.Ordinal) || normalized.StartsWith("+$", StringComparison.Ordinal))
            {
                normalized = normalized[0] + normalized[2..];
            }
            else if (normalized.StartsWith('$'))
            {
                normalized = normalized[1..];
            }

            // "1,000" / "1,234.5" kiểu Mỹ thì bỏ dấu phẩy phân cách nghìn; "1,5" đổi sang dấu chấm.
            if (normalized.Contains(','))
            {
                if (UsThousandsRegex.IsMatch(normalized) || normalized.Contains('.'))
                {
                    normalized = normalized.Replace(",", string.Empty);
                }
                else if (normalized.Count(c => c == ',') == 1)
                {
                    normalized = normalized.Replace(',', '.');
                }
            }

            return normalized;
        }

        /// <summary>Đọc số từ chuỗi: số nguyên, số thập phân, phân số "a/b", có dấu +/-.</summary>
        public static bool TryParseNumber(string? raw, out double value)
        {
            value = 0;
            var text = NormalizeNumberText(raw);
            if (text.Length == 0) return false;

            if (text.Contains('/'))
            {
                var parts = text.Split('/');
                if (parts.Length != 2) return false;
                if (!TryParseDecimal(parts[0], out var numerator)) return false;
                if (!TryParseDecimal(parts[1], out var denominator)) return false;
                if (denominator == 0) return false;

                var result = numerator / denominator;
                if (double.IsNaN(result) || double.IsInfinity(result)) return false;
                value = result;
                return true;
            }

            return TryParseDecimal(text, out value);
        }

        private static bool TryParseDecimal(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;

            // Chỉ nhận dạng số thập phân thường; chặn "1e5" và ký tự lạ để không chấm bừa.
            foreach (var ch in text)
            {
                if (!char.IsDigit(ch) && ch != '.' && ch != '-' && ch != '+') return false;
            }

            return double.TryParse(
                text,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static int GetDecimalScale(string normalized)
        {
            if (normalized.Contains('/')) return 0;
            int dot = normalized.IndexOf('.');
            if (dot < 0) return 0;
            return normalized.Length - dot - 1;
        }

        private static bool NumbersEqual(double studentNumber, double acceptedNumber, string rawStudent, string rawAccepted)
        {
            if (studentNumber.Equals(acceptedNumber)) return true;
            if (Math.Abs(studentNumber - acceptedNumber) <= Tolerance) return true;

            // Quy tắc College Board SAT: với đáp án vô hạn tuần hoàn (2/3, 1/6...) hoặc >= 4 chữ số thập phân,
            // chấp nhận học sinh cắt (truncate) hoặc làm tròn từ 3 chữ số thập phân trở lên (vd .666, .667, .166).
            var normStudent = NormalizeNumberText(rawStudent);
            var normAccepted = NormalizeNumberText(rawAccepted);
            int studentScale = GetDecimalScale(normStudent);
            int acceptedScale = GetDecimalScale(normAccepted);

            if (studentScale >= 3 && studentScale <= 10 && (normAccepted.Contains('/') || acceptedScale >= 4))
            {
                double factor = Math.Pow(10, studentScale);
                double sign = Math.Sign(acceptedNumber);
                double truncated = Math.Truncate((acceptedNumber * factor) + sign * 1e-9) / factor;
                double rounded = Math.Round(acceptedNumber, studentScale, MidpointRounding.AwayFromZero);

                if (Math.Abs(studentNumber - truncated) < 1e-9 || Math.Abs(studentNumber - rounded) < 1e-9)
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeText(string raw)
        {
            var text = (raw ?? string.Empty).Trim();
            var plain = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (!char.IsWhiteSpace(ch)) plain.Append(ch);
            }
            return plain.ToString();
        }
    }
}
