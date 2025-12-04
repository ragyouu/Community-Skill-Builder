using SkillBuilder.Models.Analytics;
namespace SkillBuilder.Models.ViewModels
{
    public class AdminProfileViewModel
    {
        public Admin Admin { get; set; }
        public List<User> Users { get; set; }
        public List<MonthlyActiveUsersDto> MonthlyActiveUsers { get; set; } = new();
        public List<User> AllUsers { get; set; }
        public List<Artisan> Artisans { get; set; }
        public List<Artisan> AllArtisans { get; set; }
        public List<TopArtisanDto> TopArtisansByEnrollments { get; set; } = new();
        public List<ArtisanApplication> PendingApplications { get; set; }
        public List<ArtisanApplication> AllPendingApplications { get; set; }
        public List<ArtisanApplication> AllApprovedApplications { get; set; }
        public List<ArtisanApplication> AllRejectedApplications { get; set; }
        public List<Course> SubmittedCourses { get; set; }
        public List<Course> AllSubmittedCourses { get; set; }
        public List<Community> Communities { get; set; }
        public List<Community> AllCommunities { get; set; }
        public List<CommunityPost> CommunitySubmissions { get; set; }
        public List<CommunityPost> AllCommunitySubmissions { get; set; }
        public List<ReportLogViewModel> ReportLogs { get; set; }
        public AnalyticsOverviewDto Analytics { get; set; } = new AnalyticsOverviewDto();
        public CommunityAnalyticsDto CommunityAnalytics { get; set; } = new CommunityAnalyticsDto();
    }
}
