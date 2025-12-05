using System;
using System.Collections.Generic;

namespace SkillBuilder.Models.Analytics
{
    public class AnalyticsOverviewDto
    {
        public int TotalLearners { get; set; }
        public int TotalArtisans { get; set; }
        public int TotalCourses { get; set; }
        public int TotalEnrollments { get; set; }
        public int UnenrolledLearners { get; set; }
        public double EnrollmentRate { get; set; }
        public double AverageArtisanRating { get; set; }
        public int CompletionRate { get; set; }
        public int EnrollmentGrowth { get; set; }
        public int PendingArtisanApplications { get; set; }
        public int PublishedCourses { get; set; }
        public int UnpublishedCourses { get; set; }

        public List<MonthlyEnrollmentDto> MonthlyEnrollments { get; set; } = new();
        public List<CategoryEnrollmentDto> CategoryEnrollments { get; set; } = new();
        public List<CategoryMonthlyEnrollmentDto> CategoryMonthlyEnrollments { get; set; } = new();
        public List<TopCourseDto> TopCourses { get; set; } = new();
        public List<CommunityPostCountDto> TopCommunities { get; set; } = new();
        public List<TopArtisanDto> TopArtisans { get; set; } = new();
        public List<TopArtisanDto> TopArtisansByEnrollments { get; set; } = new();
        public List<MonthlyEnrollmentDto> ArtisanApplicationsMonthly { get; set; } = new();
        public LearnerAnalyticsDto LearnerCharts { get; set; } = new LearnerAnalyticsDto();
    }

    public class MonthlyEnrollmentDto
    {
        public int Year { get; set; }
        public int Month { get; set; }   // 1..12
        public int Count { get; set; }

        public string Label => MonthLabel;
        public string MonthLabel => new DateTime(Year, Month, 1).ToString("MMM");
    }

    public class CategoryEnrollmentDto
    {
        public string Category { get; set; } = string.Empty;
        public int TotalStudents { get; set; }
    }

    public class CategoryMonthlyEnrollmentDto
    {
        public string Category { get; set; } = "Uncategorized";
        public int Year { get; set; }
        public int Month { get; set; }
        public int Count { get; set; }
    }

    public class TopCourseDto
    {
        public int CourseId { get; set; }
        public string Title { get; set; } = string.Empty;
        public double AverageRating { get; set; }
        public int TotalEnroll { get; set; }
        public string Category { get; set; } = "N/A";
        public bool IsPublished { get; set; }
        public int CompletedEnrollments { get; set; }
    }

    public class TopArtisanDto
    {
        public string ArtisanId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Skill { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public int TotalStudents { get; set; }
        public double AverageRating { get; set; }
        public int CoursesCount { get; set; }
    }

    public class EnrollmentByCityDto
    {
        public string City { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class LearnerAnalyticsDto
    {
        public int Male { get; set; }
        public int Female { get; set; }
        public int Others { get; set; }

        public int Age_13_17 { get; set; }
        public int Age_18_24 { get; set; }
        public int Age_25_34 { get; set; }
        public int Age_35Above { get; set; }

        public int GenZ { get; set; }
        public int Millennials { get; set; }

        public List<TopCourseDto> TopCourses { get; set; } = new();
        public List<EnrollmentByCityDto> EnrollmentByCity { get; set; } = new();

        // Computed properties for Chart.js
        public List<string> TopCourseLabels => TopCourses.Select(c => c.Title).ToList();
        public List<int> TopCourseTotalEnrollments => TopCourses.Select(c => c.TotalEnroll).ToList();

        // Example: split total enrollments 50/50 for Gen Z / Millennials
        public List<int> TopCourseGenZ { get; set; } = new();
        public List<int> TopCourseMillennials { get; set; } = new();

        public int UnenrolledStudents { get; set; } = 0;
        public double EnrollmentRate { get; set; } = 0;
    }

    public class MonthlyActiveUsersDto
    {
        public string MonthLabel { get; set; } = "";
        public int ActiveUsers { get; set; }
    }

    public class CommunityPostCountDto
    {
        public int CommunityId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int PostCount { get; set; }
    }

}