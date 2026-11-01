using LyraSAT.Data;
using LyraSAT.Models;
using Microsoft.EntityFrameworkCore;

namespace LyraSAT.Services
{
    /// <summary>
    /// Chuẩn hoá và tra cứu mã lớp học.
    /// Mã lớp là chuỗi người dùng nhập tay nên phải Trim và so sánh không phân biệt hoa/thường
    /// ở mọi chỗ: nếu không, "SF2609" và "sf2609 " sẽ thành hai lớp khác nhau và thông báo
    /// sẽ gửi sai người hoặc không gửi cho ai.
    /// </summary>
    public static class ClassCodeHelper
    {
        /// <summary>Trim mã lớp. Trả về chuỗi rỗng nếu null.</summary>
        public static string Normalize(string? classCode)
        {
            return (classCode ?? string.Empty).Trim();
        }

        /// <summary>So sánh hai mã lớp: bỏ khoảng trắng thừa và không phân biệt hoa/thường.</summary>
        public static bool SameCode(string? a, string? b)
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Tìm lớp theo mã (không phân biệt hoa/thường).</summary>
        public static Task<Class?> FindClassAsync(LyraSatDbContext context, string? classCode, bool tracking = false)
        {
            var code = Normalize(classCode);
            if (code.Length == 0) return Task.FromResult<Class?>(null);

            var lower = code.ToLower();
            var query = context.Classes.AsQueryable();
            if (!tracking) query = query.AsNoTracking();

            return query.FirstOrDefaultAsync(c => c.ClassCode.ToLower() == lower);
        }

        /// <summary>Kiểm tra lớp đã tồn tại chưa (không phân biệt hoa/thường).</summary>
        public static Task<bool> ClassExistsAsync(LyraSatDbContext context, string? classCode)
        {
            var code = Normalize(classCode);
            if (code.Length == 0) return Task.FromResult(false);

            var lower = code.ToLower();
            return context.Classes.AnyAsync(c => c.ClassCode.ToLower() == lower);
        }
    }
}
