namespace SkillBuilder.Models.ViewModels
{
    public class AchievementViewModel
    {
        public string Title { get; set; }
        public string Condition { get; set; }
        public bool IsAchieved { get; set; }
        public string AchievementKey { get; set; }
        public decimal ThreadsAwarded { get; set; }
        public decimal CurrentThreads { get; set; }
    }
}
