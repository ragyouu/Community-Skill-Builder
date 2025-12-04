namespace SkillBuilder.Models
{
    public class UserAchievement
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public string Key { get; set; }
        public DateTime DateAchieved { get; set; }
        public decimal ThreadsAwarded { get; set; } = 0; 
    }
}
