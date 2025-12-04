namespace SkillBuilder.Models.Analytics
{
    public class CommunityAnalyticsDto
    {
        public int TotalCommunities { get; set; }
        public int TotalPosts { get; set; }
        public int TotalMembers { get; set; }

        public int MembersWithPosts { get; set; }
        public int MembersWithoutPosts { get; set; }

        public int FlaggedPostsCount { get; set; }

        public List<TopCommunityDto> TopCommunities { get; set; } = new();
        public List<FlaggedPostDto> FlaggedPosts { get; set; } = new();
        public List<CommunityJoinHistoryDto> CommunityJoinHistory { get; set; } = new();
        public CommunityJoinTrendDto JoinTrend { get; set; } = new();
    }

    public class TopCommunityDto
    {
        public int CommunityId { get; set; }
        public string Name { get; set; }
        public int MembersCount { get; set; }
        public int TotalPosts { get; set; }
        public string Category { get; set; }
    }

    public class FlaggedPostDto
    {
        public int PostId { get; set; }
        public string TitleOrSnippet { get; set; }
        public string ReporterName { get; set; }
        public string Reason { get; set; }
        public DateTime ReportedAt { get; set; }
    }
    public class CommunityJoinHistoryDto
    {
        public string CommunityName { get; set; } = string.Empty;
        public int JoinCount { get; set; }
        public DateTime Date { get; set; }
    }

    public class CommunityJoinTrendDto
    {
        public List<string> Labels { get; set; } = new();
        public List<JoinTrendSeriesDto> Series { get; set; } = new();
    }

    public class JoinTrendSeriesDto
    {
        public string CommunityName { get; set; }
        public List<int> Counts { get; set; } = new();
    }
}
