using Microsoft.EntityFrameworkCore;
using SkillBuilder.Data; // Adjust if your DbContext namespace differs
using SkillBuilder.Models.Analytics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace SkillBuilder.Services
{
    public interface IAnalyticsService
    {
        Task<AnalyticsOverviewDto> GetOverviewAsync(int monthsBack = 6);
    }

    public class AnalyticsService : IAnalyticsService
    {
        private readonly AppDbContext _db;
        public AnalyticsService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<AnalyticsOverviewDto> GetOverviewAsync(int monthsBack = 6)
        {
            var result = new AnalyticsOverviewDto();

            // 1️⃣ Fetch all active learners
            var learners = await _db.Users
                .Where(u => u.Role == "Learner" && !u.IsArchived)
                .Include(u => u.Enrollments)
                .ToListAsync();

            result.TotalLearners = learners.Count;

            // 2️⃣ Unenrolled students = learners with zero enrollments
            var unenrolledStudents = learners.Count(u => !u.Enrollments.Any());
            result.LearnerCharts.UnenrolledStudents = unenrolledStudents;

            // 3️⃣ Total enrollments = total number of learners who have at least 1 enrollment
            var totalEnrolledStudents = learners.Count(u => u.Enrollments.Any());
            result.TotalEnrollments = totalEnrolledStudents;

            // 4️⃣ Enrollment rate = % of learners with at least one enrollment
            result.LearnerCharts.EnrollmentRate = learners.Count == 0
                ? 0
                : (int)Math.Round((double)totalEnrolledStudents / learners.Count * 100);

            // 5️⃣ Age calculations
            int GetAge(DateOnly birthDate) => (int)((DateTime.UtcNow - birthDate.ToDateTime(TimeOnly.MinValue)).TotalDays / 365.25);
            var learnersWithBirthdate = learners.Where(u => u.BirthDate.HasValue).ToList();

            result.LearnerCharts.Age_13_17 = learnersWithBirthdate.Count(u =>
            {
                var age = GetAge(u.BirthDate.Value);
                return age >= 13 && age <= 17;
            });
            result.LearnerCharts.Age_18_24 = learnersWithBirthdate.Count(u =>
            {
                var age = GetAge(u.BirthDate.Value);
                return age >= 18 && age <= 24;
            });
            result.LearnerCharts.Age_25_34 = learnersWithBirthdate.Count(u =>
            {
                var age = GetAge(u.BirthDate.Value);
                return age >= 25 && age <= 34;
            });
            result.LearnerCharts.Age_35Above = learnersWithBirthdate.Count(u => GetAge(u.BirthDate.Value) >= 35);

            // Gen Z = 13-24, Millennials = 25-34
            result.LearnerCharts.GenZ = result.LearnerCharts.Age_13_17 + result.LearnerCharts.Age_18_24;
            result.LearnerCharts.Millennials = result.LearnerCharts.Age_25_34;

            // 6️⃣ Top courses
            var now = DateTime.UtcNow;
            var genZCutoff = DateOnly.FromDateTime(now.AddYears(-26));       // <=26 yrs
            var millennialsMin = DateOnly.FromDateTime(now.AddYears(-42));   // 27-42 yrs
            var millennialsMax = DateOnly.FromDateTime(now.AddYears(-27));

            var topCourses = await _db.Courses
                .Where(c => !c.IsArchived)
                .Select(c => new TopCourseDto
                {
                    CourseId = c.Id,
                    Title = c.Title,
                    AverageRating = c.Reviews.Any() ? c.Reviews.Average(r => r.Rating) : 0,
                    TotalEnroll = c.Enrollments.Count(),
                    CompletedEnrollments = c.Enrollments.Count(e => e.IsCompleted),
                    Category = c.Category,
                    IsPublished = c.IsPublished
                })
                .OrderByDescending(c => c.TotalEnroll)
                .ThenByDescending(c => c.AverageRating)
                .ToListAsync();

            result.TopCourses = topCourses;
            result.LearnerCharts.TopCourses = topCourses;

            // Gen Z per course
            result.LearnerCharts.TopCourseGenZ = topCourses
                .Select(c => _db.Enrollments
                    .Include(e => e.User)
                    .Count(e => e.CourseId == c.CourseId
                                && e.User.BirthDate != null
                                && e.User.BirthDate.Value >= genZCutoff))
                .ToList();

            // Millennials per course
            result.LearnerCharts.TopCourseMillennials = topCourses
                .Select(c => _db.Enrollments
                    .Include(e => e.User)
                    .Count(e => e.CourseId == c.CourseId
                                && e.User.BirthDate != null
                                && e.User.BirthDate.Value >= millennialsMin
                                && e.User.BirthDate.Value <= millennialsMax))
                .ToList();

            // 7️⃣ Other stats
            result.TotalArtisans = await _db.Artisans.CountAsync(a => !a.IsArchived);

            // Total courses regardless of archived
            var allCourses = await _db.Courses
                .Where(c => !c.IsArchived)
                .ToListAsync();

            // Published vs Unpublished
            result.PublishedCourses = allCourses.Count(c => c.IsPublished);
            result.UnpublishedCourses = allCourses.Count(c => !c.IsPublished);
            result.TotalCourses = allCourses.Count;

            // Completion rate & monthly enrollments
            var start = new DateTime(now.Year, now.Month, 1).AddMonths(-(monthsBack - 1));
            var end = new DateTime(now.Year, now.Month, now.Day, 23, 59, 59, 999, DateTimeKind.Utc);

            var filteredEnrollments = await _db.Enrollments
                .Include(e => e.User)
                .Where(e => e.EnrolledAt >= start && e.EnrolledAt <= end)
                .ToListAsync();

            // --- Artisan applications monthly (onboarding trend) ---
            var artisanApplicationsInRange = await _db.ArtisanApplications
                .Where(a => a.SubmittedAt >= start && a.SubmittedAt <= end)
                .ToListAsync();

            var artisanAppGroups = artisanApplicationsInRange
                .GroupBy(a => new { a.SubmittedAt.Year, a.SubmittedAt.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToList();

            var artisanAppMonthList = new List<MonthlyEnrollmentDto>();
            for (int i = 0; i < monthsBack; i++)
            {
                var dt = start.AddMonths(i);
                var found = artisanAppGroups.FirstOrDefault(m => m.Year == dt.Year && m.Month == dt.Month);
                artisanAppMonthList.Add(found != null
                    ? new MonthlyEnrollmentDto { Year = found.Year, Month = found.Month, Count = found.Count }
                    : new MonthlyEnrollmentDto { Year = dt.Year, Month = dt.Month, Count = 0 });
            }
            result.ArtisanApplicationsMonthly = artisanAppMonthList;

            var completedEnrollments = filteredEnrollments.Count(e => e.IsCompleted);
            result.CompletionRate = filteredEnrollments.Count == 0 ? 0 : (int)Math.Round((double)completedEnrollments / filteredEnrollments.Count * 100);

            var monthlyGroups = filteredEnrollments
                .GroupBy(e => new { e.EnrolledAt.Year, e.EnrolledAt.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .ToList();

            result.EnrollmentGrowth = monthlyGroups.Count >= 2
                ? (monthlyGroups[monthlyGroups.Count - 2].Count == 0
                    ? (monthlyGroups.Last().Count > 0 ? 100 : 0)
                    : (int)Math.Round((double)(monthlyGroups.Last().Count - monthlyGroups[monthlyGroups.Count - 2].Count) / monthlyGroups[monthlyGroups.Count - 2].Count * 100))
                : 0;

            var monthList = new List<MonthlyEnrollmentDto>();
            for (int i = 0; i < monthsBack; i++)
            {
                var dt = start.AddMonths(i);
                var found = monthlyGroups.FirstOrDefault(m => m.Year == dt.Year && m.Month == dt.Month);
                monthList.Add(found != null
                    ? new MonthlyEnrollmentDto { Year = found.Year, Month = found.Month, Count = found.Count }
                    : new MonthlyEnrollmentDto { Year = dt.Year, Month = dt.Month, Count = 0 });
            }

            result.MonthlyEnrollments = monthList;

            // --- Category enrollments ---
            var TOP_CATEGORIES = new[] { "Pottery", "Weaving", "Shoemaking", "Woodcarving", "Embroidery" };

            // Category monthly enrollments
            var categoryMonthlyDto = _db.Enrollments
                .Where(e => e.EnrolledAt >= start && e.EnrolledAt <= end)
                .Join(_db.Courses, e => e.CourseId, c => c.Id, (e, c) => new { c.Category, e.EnrolledAt })
                .AsEnumerable()
                .GroupBy(x =>
                {
                    var cat = string.IsNullOrWhiteSpace(x.Category) ? "Others" : x.Category;
                    return TOP_CATEGORIES.Contains(cat) ? cat : "Others";
                })
                .SelectMany(g => g.GroupBy(e => new { e.EnrolledAt.Year, e.EnrolledAt.Month, Category = g.Key })
                                  .Select(m => new CategoryMonthlyEnrollmentDto
                                  {
                                      Year = m.Key.Year,
                                      Month = m.Key.Month,
                                      Category = m.Key.Category,
                                      Count = m.Count()
                                  }))
                .ToList();

            // Fill missing months
            var months = Enumerable.Range(0, monthsBack).Select(i => start.AddMonths(i)).ToList();
            foreach (var cat in TOP_CATEGORIES.Append("Others"))
            {
                foreach (var m in months)
                {
                    if (!categoryMonthlyDto.Any(x => x.Category == cat && x.Year == m.Year && x.Month == m.Month))
                    {
                        categoryMonthlyDto.Add(new CategoryMonthlyEnrollmentDto
                        {
                            Category = cat,
                            Year = m.Year,
                            Month = m.Month,
                            Count = 0
                        });
                    }
                }
            }
            result.CategoryMonthlyEnrollments = categoryMonthlyDto;

            // Category totals (number of courses per category)
            var categoryCourseCounts = await _db.Courses
                .Where(c => !c.IsArchived)
                .GroupBy(c => string.IsNullOrWhiteSpace(c.Category) ? "Others" : c.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .ToListAsync();

            // Map into CategoryEnrollmentDto (TotalStudents now represents # of courses)
            result.CategoryEnrollments = categoryCourseCounts
                .Select(x =>
                {
                    var cat = TOP_CATEGORIES.Contains(x.Category) ? x.Category : "Others";
                    return new CategoryEnrollmentDto
                    {
                        Category = cat,
                        TotalStudents = x.Count // now number of courses
                    };
                })
                .GroupBy(c => c.Category) // merge Others duplicates if any
                .Select(g => new CategoryEnrollmentDto
                {
                    Category = g.Key,
                    TotalStudents = g.Sum(x => x.TotalStudents)
                })
                .OrderByDescending(c => c.TotalStudents)
                .ToList();

            // --- Artisans: courses count and enrollments count ---
            var artisansRaw = await _db.Artisans
                .Where(a => !a.IsArchived)
                .Select(a => new
                {
                    a.ArtisanId,
                    Name = (a.FirstName + " " + a.LastName).Trim(),
                    Skill = a.Profession,
                    Location = a.Hometown,
                    CoursesCount = a.Courses.Count(), // number of courses artisan has
                    EnrollmentsCount = a.Courses.Sum(c => c.Enrollments.Count()) // total enrollments across their courses
                })
                .ToListAsync();

            // Top  by enrollments (dashboard top 5)
            var topArtisansByEnrollmentsRaw = artisansRaw
                .OrderByDescending(a => a.EnrollmentsCount)
                .Take(5)
                .ToList();

            // ratings
            var artisanIds = artisansRaw.Select(a => a.ArtisanId).ToList();
            var ratings = await _db.Courses
                .Where(c => artisanIds.Contains(c.CreatedBy))
                .SelectMany(c => c.Reviews)
                .GroupBy(r => r.Course.CreatedBy)
                .Select(g => new { ArtisanId = g.Key, AvgRating = g.Average(r => r.Rating) })
                .ToListAsync();

            // Map full list (no .Take())
            result.TopArtisans = artisansRaw
                .Select(a =>
                {
                    var avgRating = ratings.FirstOrDefault(r => r.ArtisanId == a.ArtisanId)?.AvgRating ?? 0;
                    return new TopArtisanDto
                    {
                        ArtisanId = a.ArtisanId,
                        Name = a.Name,
                        Skill = a.Skill,
                        Location = a.Location,
                        TotalStudents = a.EnrollmentsCount,       // enrollments
                        AverageRating = Math.Round(avgRating, 2),
                        CoursesCount = a.CoursesCount             // number of courses
                    };
                })
                .OrderByDescending(a => a.TotalStudents)
                .ToList();

            // TopArtisansByEnrollments (for table & charts)
            result.TopArtisansByEnrollments = topArtisansByEnrollmentsRaw
                .Select(a =>
                {
                    var avgRating = ratings.FirstOrDefault(r => r.ArtisanId == a.ArtisanId)?.AvgRating ?? 0;
                    return new TopArtisanDto
                    {
                        ArtisanId = a.ArtisanId,
                        Name = a.Name,
                        Skill = a.Skill,
                        Location = a.Location,
                        TotalStudents = a.EnrollmentsCount,
                        AverageRating = Math.Round(avgRating, 2),
                        CoursesCount = a.CoursesCount
                    };
                })
                .ToList();

            // Average artisan rating
            var artisanRatings = await _db.Artisans
                .Where(a => !a.IsArchived)
                .SelectMany(a => a.Courses)
                .SelectMany(c => c.Reviews)
                .ToListAsync();

            result.AverageArtisanRating = artisanRatings.Any()
                ? Math.Round(artisanRatings.Average(r => r.Rating), 1)
                : 0;

            result.PendingArtisanApplications = await _db.ArtisanApplications
                .CountAsync(a => a.Status == "Pending");

            var monthlyActiveUsers = new List<MonthlyActiveUsersDto>();

            var currentYear = DateTime.Now.Year;

            for (int month = 1; month <= 12; month++)
            {
                var activeUsersCount = _db.Users
                    .Where(u =>
                        u.Enrollments.Any(e => e.EnrolledAt.Year == currentYear && e.EnrolledAt.Month == month) ||
                        u.Reviews.Any(r => r.CreatedAt.Year == currentYear && r.CreatedAt.Month == month) ||
                        u.ProjectSubmissions.Any(p => p.SubmittedAt.Year == currentYear && p.SubmittedAt.Month == month) ||
                        u.Memberships.Any(m => m.JoinedAt.Year == currentYear && m.JoinedAt.Month == month)
                    )
                    .Count();

                monthlyActiveUsers.Add(new MonthlyActiveUsersDto
                {
                    MonthLabel = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(month),
                    ActiveUsers = activeUsersCount
                });
            }

            var topCommunities = await _db.Communities
                .Where(c => !c.IsArchived && c.IsPublished)
                .Select(c => new CommunityPostCountDto
                {
                    CommunityId = c.Id,
                    Name = c.Name,
                    PostCount = c.Posts.Count(p => p.IsPublished)
                })
                .OrderByDescending(c => c.PostCount)
                .Take(5)
                .ToListAsync();

            result.TopCommunities = topCommunities;

            return result;
        }
    }
}