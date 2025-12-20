namespace SkillBuilder.Models.ViewModels
{
    public class InteractiveContentViewModel
    {
        public int Id { get; set; }  // ✅ added
        public string ContentType { get; set; } = ""; // "Text", "Link", "Reflection", "Question"
        public string ContentText { get; set; } = "";

        // Only for Question type
        public string? OptionA { get; set; }
        public string? OptionB { get; set; }
        public string? OptionC { get; set; }
        public string? OptionD { get; set; }
        public string? CorrectAnswer { get; set; }
        public int? ReflectionMinChars { get; set; }

        public IFormFile? MediaFile { get; set; }
        public string? ExistingMediaUrl { get; set; }
    }
}
