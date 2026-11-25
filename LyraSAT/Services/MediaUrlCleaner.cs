using LyraSAT.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LyraSAT.Services
{
    /// <summary>
    /// Chỉ xóa object trên Supabase Storage khi không còn bản ghi DB nào tham chiếu tới URL đó,
    /// tránh xóa nhầm file đang được dùng chung giữa Test.VideoExplanationUrl và TestVideos (hoặc bảng khác).
    /// </summary>
    public static class MediaUrlCleaner
    {
        private static async Task<bool> IsReferencedInDbAsync(
            LyraSatDbContext context,
            string url,
            CancellationToken ct)
        {
            if (await context.Tests.AsNoTracking().AnyAsync(t => t.VideoExplanationUrl == url, ct))
                return true;
            if (await context.TestVideos.AsNoTracking().AnyAsync(v => v.Url == url, ct))
                return true;
            if (await context.Questions.AsNoTracking().AnyAsync(q => q.VideoExplanationUrl == url, ct))
                return true;
            if (await context.QuestionComments.AsNoTracking().AnyAsync(c => c.AttachmentUrl == url, ct))
                return true;
            if (await context.QuestionAssets.AsNoTracking().AnyAsync(a => a.FileUrl == url, ct))
                return true;
            return false;
        }

        public static async Task<bool> DeleteIfUnusedAsync(
            LyraSatDbContext context,
            ISupabaseStorageService storage,
            string? oldUrl,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(oldUrl)) return false;
            var trimmed = oldUrl.Trim();
            if (storage.TryExtractBucketAndPath(trimmed) == null) return false;

            if (await IsReferencedInDbAsync(context, trimmed, ct))
            {
                return false;
            }

            return await storage.DeleteByUrlAsync(trimmed, ct);
        }

        public static async Task DeleteManyIfUnusedAsync(
            LyraSatDbContext context,
            ISupabaseStorageService storage,
            IEnumerable<string?> urls,
            int maxConcurrency = 5,
            CancellationToken ct = default)
        {
            var candidates = urls
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u!.Trim())
                .Where(u => storage.TryExtractBucketAndPath(u) != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 0) return;

            // DbContext không thread-safe: kiểm tra DB tuần tự trước khi xóa song song qua HTTP
            var unusedUrls = new List<string>(candidates.Count);
            foreach (var candidate in candidates)
            {
                if (!await IsReferencedInDbAsync(context, candidate, ct))
                {
                    unusedUrls.Add(candidate);
                }
            }

            if (unusedUrls.Count == 0) return;

            using var gate = new SemaphoreSlim(Math.Max(1, maxConcurrency));
            await Task.WhenAll(unusedUrls.Select(async url =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    await storage.DeleteByUrlAsync(url, ct);
                }
                finally
                {
                    gate.Release();
                }
            }));
        }
    }
}
