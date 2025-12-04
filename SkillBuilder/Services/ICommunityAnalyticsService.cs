using SkillBuilder.Data;
using SkillBuilder.Models.Analytics;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace SkillBuilder.Services
{
    public interface ICommunityAnalyticsService
    {
        Task<CommunityAnalyticsDto> GetAnalyticsAsync(string range);
    }

    public class CommunityAnalyticsService : ICommunityAnalyticsService
    {
        private readonly AppDbContext _db;

        public CommunityAnalyticsService(AppDbContext db)
        {
            _db = db;
        }

        public static class DateRangeHelper
        {
            public static (DateTime start, DateTime end) GetRange(string range)
            {
                var now = DateTime.UtcNow.Date;

                return range switch
                {
                    "today" => (now, now.AddDays(1)),
                    "yesterday" => (now.AddDays(-1), now),
                    "lastweek" => (now.AddDays(-7), now),
                    "lastmonth" => (now.AddMonths(-1), now),
                    "lastyear" => (now.AddYears(-1), now),
                    _ => (now.AddMonths(-1), now) // default
                };
            }
        }

        public async Task<CommunityAnalyticsDto> GetAnalyticsAsync(string range)
        {
            var (start, end) = DateRangeHelper.GetRange(range);

            var dto = new CommunityAnalyticsDto();

            // --- Summary counts ---
            dto.TotalCommunities = await _db.Communities.CountAsync(c => !c.IsArchived);
            dto.TotalPosts = await _db.CommunityPosts.CountAsync(p => p.IsPublished);
            dto.TotalMembers = await _db.CommunityMemberships.CountAsync();

            var communityIds = await _db.Communities
                .Where(c => !c.IsArchived)
                .Select(c => c.Id)
                .ToListAsync();

            dto.MembersWithPosts = await _db.CommunityMemberships
                .Where(m => communityIds.Contains(m.CommunityId))
                .Where(m => _db.CommunityPosts.Any(p => p.AuthorId == m.UserId && p.CommunityId == m.CommunityId))
                .Select(m => m.UserId)
                .Distinct()
                .CountAsync();

            dto.MembersWithoutPosts = Math.Max(0, dto.TotalMembers - dto.MembersWithPosts);

            dto.FlaggedPostsCount = await _db.CommunityPostReports.CountAsync();

            // --- Top communities by members ---
            dto.TopCommunities = await _db.Communities
                .Where(c => !c.IsArchived)
                .Select(c => new TopCommunityDto
                {
                    CommunityId = c.Id,
                    Name = c.Name,
                    MembersCount = c.Memberships.Count,
                    TotalPosts = c.Posts.Count,
                    Category = c.Category
                })
                .OrderByDescending(c => c.MembersCount)
                .Take(5)
                .ToListAsync();

            // --- Flagged posts details ---
            dto.FlaggedPosts = await _db.CommunityPostReports
                .Include(r => r.Post)
                .Include(r => r.Post.Author)
                .Select(r => new FlaggedPostDto
                {
                    PostId = r.PostId,
                    TitleOrSnippet = !string.IsNullOrEmpty(r.Post.Title)
                        ? r.Post.Title
                        : r.Post.Content.Length > 40 ? r.Post.Content.Substring(0, 40) + "..." : r.Post.Content,
                    ReporterName = _db.Users
                        .Where(u => u.Id == r.ReporterId)
                        .Select(u => u.FirstName + " " + u.LastName)
                        .FirstOrDefault() ?? "Unknown",
                    Reason = r.Reason,
                    ReportedAt = r.ReportedAt
                })
                .OrderByDescending(r => r.ReportedAt)
                .ToListAsync();

            var dates = Enumerable.Range(0, (end - start).Days)
                      .Select(d => start.AddDays(d))
                      .ToList();

            var joinHistory = await _db.CommunityJoinRequests
                .Where(j => j.RequestedAt >= start && j.RequestedAt < end)
                .GroupBy(j => new { j.Community.Name, Date = j.RequestedAt.Date })
                .Select(g => new CommunityJoinHistoryDto
                {
                    CommunityName = g.Key.Name,
                    Date = g.Key.Date,
                    JoinCount = g.Count()
                })
                .ToListAsync();

            var grouped = joinHistory
                .GroupBy(x => x.CommunityName)
                .Select(g => new JoinTrendSeriesDto
                {
                    CommunityName = g.Key,
                    Counts = dates.Select(d =>
                        g.FirstOrDefault(j => j.Date.Date == d.Date)?.JoinCount ?? 0
                    ).ToList()
                })
                .ToList();

            dto.JoinTrend = new CommunityJoinTrendDto
            {
                Labels = dates.Select(d => d.ToString("MMM dd")).ToList(),
                Series = grouped
            };

            return dto;
        }
    }
}