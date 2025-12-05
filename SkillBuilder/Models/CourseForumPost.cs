namespace SkillBuilder.Models
{
    public class CourseForumPost
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        public string UserId { get; set; }
        public string Content { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public User User { get; set; }
        public Course Course { get; set; }
    }
}
