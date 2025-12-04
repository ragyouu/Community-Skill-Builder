namespace SkillBuilder.Models.ViewModels
{
    public class CourseBuilderViewModel
    {
        public Course? Course { get; set; } = new();
        public int DurationValue { get; set; }
        public string DurationUnit { get; set; } = "minutes";
        public IFormFile? ImageFile { get; set; }
        public IFormFile? VideoFile { get; set; }
        public IFormFile? ThumbnailFile { get; set; }
        public List<string> LearningObjectives { get; set; } = new List<string> { "" };
        public FinalProjectViewModel? FinalProject { get; set; } = new();
        public List<ArtisanWorkViewModel> ArtisanWorks { get; set; } = new();
        public List<CourseModuleViewModel> Modules { get; set; } = new List<CourseModuleViewModel>();
        public List<CourseMaterialViewModel> Materials { get; set; } = new List<CourseMaterialViewModel>();
        public List<int> MaterialsToDelete { get; set; } = new();
        public string? CustomCategory { get; set; }
        public bool IsFree { get; set; } = true;
        public decimal DesiredThreads { get; set; } = 0.00M;
        public List<InteractiveContentViewModel> Interactives { get; set; } = new();
    }
}
