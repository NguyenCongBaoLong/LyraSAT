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
        private static readonly Regex DirectVideoExtRegex = new(
            @"\.(mp4|m4v|webm|mov|ogv)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static bool HostMatches(string host, string domain) =>
            host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

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

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return "Link phải bắt đầu bằng http:// hoặc https://.";
            }

            var host = uri.Host;

            // Google Docs/Slides/Sheets và thư mục Drive bị Google chặn nhúng -> khung đen.
            if (HostMatches(host, "docs.google.com") ||
                HostMatches(host, "slides.google.com") ||
                HostMatches(host, "sheets.google.com") ||
                (HostMatches(host, "drive.google.com") && uri.AbsolutePath.StartsWith("/drive/", StringComparison.OrdinalIgnoreCase)))
            {
                return "Link Google Docs/Slides/Drive folder không phát được trong khung video. " +
                       "Hãy mở file video bằng Drive rồi lấy link chia sẻ của chính file video đó.";
            }

            if (HostMatches(host, "youtube.com") ||
                HostMatches(host, "youtu.be") ||
                HostMatches(host, "drive.google.com") ||
                HostMatches(host, "vimeo.com") ||
                HostMatches(host, "loom.com"))
            {
                return null;
            }

            // Link video trực tiếp (.mp4, .webm...)
            if (DirectVideoExtRegex.IsMatch(uri.AbsolutePath))
            {
                return null;
            }

            return "Link này chưa được hỗ trợ. Dùng link YouTube, Google Drive (file video) hoặc file mp4/webm.";
        }
    }
}
