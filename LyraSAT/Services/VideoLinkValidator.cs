using System;
using System.Text.RegularExpressions;

namespace LyraSAT.Services
{
    /// <summary>
    /// Kiểm tra một link video chữa bài có phát được trong khung nhúng hay không.
    /// Dùng chung cho video cấp đề (TestVideosController) và video cấp câu hỏi
    /// (QuestionsController) để hai nơi không lệch luật nhau.
    /// </summary>
    public static class VideoLinkValidator
    {
        /// <summary>
        /// Trả về null nếu link dùng được, hoặc câu thông báo lỗi bằng tiếng Việt.
        /// </summary>
        public static string? Validate(string? rawUrl)
        {
            var url = rawUrl?.Trim() ?? string.Empty;
            if (url.Length == 0)
            {
                return "Hãy dán link video hoặc tải file lên.";
            }

            // File đã upload nội bộ
            if (url.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "Link phải bắt đầu bằng http:// hoặc https://.";
            }

            // Google Docs/Slides/Sheets và thư mục Drive bị Google chặn nhúng -> khung đen.
            if (url.Contains("docs.google.com") || url.Contains("slides.google.com") ||
                url.Contains("sheets.google.com") || url.Contains("drive.google.com/drive/"))
            {
                return "Link Google Docs/Slides/Drive folder không phát được trong khung video. " +
                       "Hãy mở file video bằng Drive rồi lấy link chia sẻ của chính file video đó.";
            }

            if (url.Contains("youtube.com") || url.Contains("youtu.be"))
            {
                return null;
            }

            // Link Drive dạng file: /file/d/<id>/view hoặc open?id=...
            if (url.Contains("drive.google.com"))
            {
                return null;
            }

            // Link video trực tiếp (.mp4, .webm...) hoặc dịch vụ nhúng phổ biến
            if (Regex.IsMatch(url, @"\.(mp4|m4v|webm|mov|ogv)(\?.*)?$", RegexOptions.IgnoreCase))
            {
                return null;
            }

            return "Link này chưa được hỗ trợ. Dùng link YouTube, Google Drive (file video) hoặc file mp4/webm.";
        }
    }
}
