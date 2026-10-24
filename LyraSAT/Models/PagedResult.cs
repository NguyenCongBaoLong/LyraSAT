using Microsoft.EntityFrameworkCore;

namespace LyraSAT.Models
{
    public class PagedResult<T>
    {
        public int Page { get; init; }
        public int PageSize { get; init; }
        public int TotalCount { get; init; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    }

    public static class PaginationExtensions
    {
        public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
            this IQueryable<T> query,
            int page,
            int pageSize)
        {
            var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalCount / normalizedPageSize);
            var normalizedPage = totalPages == 0
                ? 1
                : Math.Clamp(page, 1, totalPages);
            var items = await query
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToListAsync();

            return new PagedResult<T>
            {
                Page = normalizedPage,
                PageSize = normalizedPageSize,
                TotalCount = totalCount,
                Items = items
            };
        }
    }
}
