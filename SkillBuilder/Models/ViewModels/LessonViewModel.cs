namespace SkillBuilder.Models.ViewModels
{
    public class LessonViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string LessonType { get; set; } = string.Empty;
        public int DurationValue { get; set; }
        public string DurationUnit { get; set; } = "minutes";
        public string ContentText { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string? VideoUrl { get; set; }
        public string? ExistingImageUrl { get; set; }
        public string? ExistingVideoUrl { get; set; }
        public IFormFile? ImageFile { get; set; }
        public IFormFile? VideoFile { get; set; }
        public List<InteractiveContentViewModel> InteractiveContents { get; set; } = new();
        public List<QuizQuestionViewModel> QuizQuestions { get; set; } = new();
    }
}
