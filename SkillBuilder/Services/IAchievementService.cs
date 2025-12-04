using SkillBuilder.Data;
using SkillBuilder.Models;
using SkillBuilder.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace SkillBuilder.Services
{
    public interface IAchievementService
    {
        Task<AchievementViewModel> AwardAchievementAsync(string userId, string achievementKey);
        Task<List<AchievementViewModel>> GetUserAchievementsAsync(string userId);
    }

    public class AchievementService : IAchievementService
    {
        private readonly AppDbContext _context;

        public AchievementService(AppDbContext context)
        {
            _context = context;
        }

        // Award achievement & update user's threads
        public async Task<AchievementViewModel> AwardAchievementAsync(string userId, string achievementKey)
        {
            // Check if user already has this achievement
            var existing = await _context.UserAchievements
                .FirstOrDefaultAsync(a => a.UserId == userId && a.Key == achievementKey);

            if (existing != null)
            {
                // Return the existing achievement with up-to-date threads
                var totalThreadsExisting = await _context.UserAchievements
                    .Where(a => a.UserId == userId)
                    .SumAsync(a => a.ThreadsAwarded);

                return new AchievementViewModel
                {
                    Title = GetAchievementTitle(existing.Key),
                    Condition = GetAchievementDescription(existing.Key),
                    IsAchieved = true,
                    ThreadsAwarded = existing.ThreadsAwarded,
                    CurrentThreads = totalThreadsExisting
                };
            }

            // Determine how many Threads this achievement gives
            var threads = GetAchievementThreads(achievementKey);

            // Create achievement
            var achievement = new UserAchievement
            {
                UserId = userId,
                Key = achievementKey,
                DateAchieved = DateTime.UtcNow,
                ThreadsAwarded = threads
            };

            _context.UserAchievements.Add(achievement);
            await _context.SaveChangesAsync();

            // Calculate total threads for the user
            var totalThreads = await _context.UserAchievements
                .Where(a => a.UserId == userId)
                .SumAsync(a => a.ThreadsAwarded);

            // Optional: update the user's Threads column to reflect the sum
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user != null)
            {
                user.Threads = totalThreads;
                await _context.SaveChangesAsync();
            }

            return new AchievementViewModel
            {
                Title = GetAchievementTitle(achievementKey),
                Condition = GetAchievementDescription(achievementKey),
                IsAchieved = true,
                ThreadsAwarded = threads,
                CurrentThreads = totalThreads
            };
        }

        // Get all achievements with up-to-date threads
        public async Task<List<AchievementViewModel>> GetUserAchievementsAsync(string userId)
        {
            var userAchievements = await _context.UserAchievements
                .Where(a => a.UserId == userId)
                .ToListAsync();

            var totalThreads = userAchievements.Sum(a => a.ThreadsAwarded);

            return userAchievements
                .Select(a => new AchievementViewModel
                {
                    Title = GetAchievementTitle(a.Key),
                    Condition = GetAchievementDescription(a.Key),
                    IsAchieved = true,
                    ThreadsAwarded = a.ThreadsAwarded,
                    CurrentThreads = totalThreads
                })
                .ToList();
        }

        private string GetAchievementTitle(string key) => key switch
        {
            "FirstCourseEnrolled" => "First Course Enrolled",
            "ThreeCoursesEnrolled" => "Lifelong Learner",
            "CourseCompleted" => "Course Completed",
            "ProjectSubmitted" => "Project Submitted",
            "FirstReviewSubmitted" => "First Review Submitted",
            _ => key
        };

        private string GetAchievementDescription(string key) => key switch
        {
            "FirstCourseEnrolled" => "Enroll in your first course",
            "ThreeCoursesEnrolled" => "Enroll in 3 courses",
            "CourseCompleted" => "Complete a course",
            "ProjectSubmitted" => "Submit your first project",
            "FirstReviewSubmitted" => "Submit your first review",
            _ => ""
        };

        private decimal GetAchievementThreads(string key) => key switch
        {
            "FirstCourseEnrolled" => 20M,
            "ThreeCoursesEnrolled" => 50M,
            "CourseCompleted" => 30M,
            "ProjectSubmitted" => 70M,
            "FirstReviewSubmitted" => 10M,
            _ => 0M
        };
    }
}