namespace SkillBuilder.Models
{
    public class InteractiveContent
    {
        public int Id { get; set; }
        public int ModuleContentId { get; set; }
        public ModuleContent ModuleContent { get; set; } = null!;
        public string ContentType { get; set; } = ""; // "Text", "Link", "Reflection", "Question"
        public string ContentText { get; set; } = "";
        public int? ReflectionMinChars { get; set; }

        // Only for Question type
        public string? OptionA { get; set; }
        public string? OptionB { get; set; }
        public string? OptionC { get; set; }
        public string? OptionD { get; set; }
        public string? CorrectAnswer { get; set; }
    }
}
