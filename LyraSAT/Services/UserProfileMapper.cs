using LyraSAT.Data;
using LyraSAT.Models;
using Microsoft.EntityFrameworkCore;

namespace LyraSAT.Services
{
    /// <summary>
    /// Chỉ trả về các trường an toàn của người dùng cho client, không bao giờ trả PasswordHash.
    /// </summary>
    public static class UserProfileMapper
    {
        public static async Task<object> ToSafeUserAsync(LyraSatDbContext context, User u)
        {
            var classes = u.Role == "Student"
                ? (await context.StudentClasses
                    .AsNoTracking()
                    .Where(sc => sc.UserId == u.Id && sc.Class != null)
                    .OrderBy(sc => sc.AssignedAt)
                    .Select(sc => new
                    {
                        classCode = sc.Class!.ClassCode,
                        className = sc.Class.ClassName
                    })
                    .ToListAsync())
                    .Cast<object>()
                    .ToList()
                : new List<object>();

            return new
            {
                u.Id,
                u.Email,
                u.Role,
                u.FullName,
                u.ClassCode,
                Classes = classes,
                u.Avatar,
                u.Dob,
                u.Nickname,
                u.TargetScore,
                u.TuitionStatus,
                u.IsActive,
                u.StreakCount,
                u.LastLoginDate,
                u.ExamDate,
                u.School
            };
        }
    }
}
