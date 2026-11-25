using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LyraSAT.Services
{
    /// <summary>
    /// Kiểm tra và chuẩn hóa link/file video chữa bài.
    /// Dùng chung cho video cấp đề (TestsController, TestVideosController) và cấp câu hỏi
    /// (QuestionsController) để các luồng thống nhất hoàn toàn.
    /// </summary>
    public static class VideoLinkValidator
    {
        public const long MaxVideoBytes = 150L * 1024 * 1024;
        private static readonly Regex SchemePrefixRegex = new(
            @"^[A-Za-z][A-Za-z0-9+.-]*:",
            RegexOptions.Compiled);
        private static readonly string[] DisallowedSchemePrefixes =
        {
            "javascript:", "data:", "vbscript:", "file:", "mailto:", "tel:", "ftp:"
        };

        /// <summary>
        /// Chuẩn hóa đường dẫn người dùng dán vào (tự thêm https:// nếu dán thiếu giao thức).
        /// </summary>
        public static string Normalize(string? rawUrl)
        {
            var url = rawUrl?.Trim() ?? string.Empty;
            if (url.Length == 0)
            {
                return string.Empty;
            }

            if (url.StartsWith("//", StringComparison.Ordinal))
            {
                return "https:" + url;
            }

            if (url.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            if (!SchemePrefixRegex.IsMatch(url) &&
                !DisallowedSchemePrefixes.Any(prefix => url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
                url.Contains('.', StringComparison.Ordinal) &&
                !url.Any(ch => char.IsWhiteSpace(ch) || char.IsControl(ch)))
            {
                return "https://" + url;
            }

            return url;
        }

        /// <summary>
        /// Trả về null nếu link dùng được (YouTube công khai/không công khai/Shorts, Drive, Vimeo, Loom, URL https hợp lệ...),
        /// hoặc câu thông báo lỗi bằng tiếng Việt.
        /// </summary>
        public static string? Validate(string? rawUrl)
        {
            var url = Normalize(rawUrl);
            if (url.Length == 0)
            {
                return "Hãy dán link video hoặc tải file lên.";
            }

            if (url.Length > 2048)
            {
                return "Link video quá dài (tối đa 2048 ký tự).";
            }

            if (url.Any(ch => char.IsWhiteSpace(ch) || char.IsControl(ch)))
            {
                return "Link video không hợp lệ (chứa khoảng trắng hoặc ký tự điều khiển).";
            }

            // Giữ tương thích với đường dẫn file nội bộ cũ nếu còn trong DB
            if (url.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            {
                if (url.Contains("..", StringComparison.Ordinal) ||
                    url.IndexOfAny(['\\', '"', '\'', '<', '>']) >= 0)
                {
                    return "Đường dẫn nội bộ không hợp lệ.";
                }
                return null;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                (!uri.Host.Contains('.', StringComparison.Ordinal) &&
                 !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
                 uri.HostNameType != UriHostNameType.IPv6))
            {
                return "Link không hợp lệ. Hãy dán đúng đường dẫn bắt đầu bằng http:// hoặc https://.";
            }

            return null;
        }

        public static string GetVideoContentType(string ext) => ext switch
        {
            ".mp4" or ".m4v" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".ogv" => "video/ogg",
            _ => "application/octet-stream"
        };

        /// <summary>
        /// Kiểm tra file video upload dùng chung và trả về extension + MIME content-type chuẩn.
        /// </summary>
        public static string? ValidateVideoFile(IFormFile? file, out string ext, out string contentType)
        {
            ext = string.Empty;
            contentType = "application/octet-stream";

            if (file == null || file.Length == 0)
            {
                return "Không tìm thấy file video tải lên.";
            }

            if (file.Length > MaxVideoBytes)
            {
                return "File video vượt quá giới hạn 150MB.";
            }

            ext = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
            contentType = GetVideoContentType(ext);
            if (contentType == "application/octet-stream")
            {
                return "Định dạng video không hỗ trợ. Chỉ nhận mp4, m4v, webm, mov, ogv.";
            }

            return null;
        }
    }
}
