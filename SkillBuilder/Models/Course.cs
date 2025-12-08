using System.ComponentModel.DataAnnotations.Schema;

namespace SkillBuilder.Models
{
    public class Course
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Link { get; set; }
        public string? ImageUrl { get; set; }
        public string Overview { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;
        public string? Video { get; set; }
        public string? Thumbnail { get; set; }
        public string? WhatToLearn { get; set; }
        public string FullDescription { get; set; } = string.Empty;
        public string Requirements { get; set; } = string.Empty;
        public string FinalProjectTitle { get; set; } = string.Empty;
        public string FinalProjectDescription { get; set; } = string.Empty;
        public bool IsFree { get; set; } = true;
        public decimal? DesiredThreads { get; set; } = 0.00M;

        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsPublished { get; set; } = true;

        [ForeignKey("CreatedBy")]
        public Artisan? Artisan { get; set; }

        public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
        public ICollection<ArtisanWork> ArtisanWorks { get; set; } = new List<ArtisanWork>();
        public ICollection<CourseReview> Reviews { get; set; } = new List<CourseReview>();
        public ICollection<CourseModule> CourseModules { get; set; } = new List<CourseModule>();
        public ICollection<CourseMaterial> Materials { get; set; } = new List<CourseMaterial>();
        public ICollection<CourseProjectSubmission> ProjectSubmissions { get; set; } = new List<CourseProjectSubmission>();

        [NotMapped]
        public int UserCount => Enrollments?.Count ?? 0;

        [NotMapped]
        public int TotalModules => CourseModules?.Count ?? 0;

        [NotMapped]
        public double AverageRating => (Reviews != null && Reviews.Any()) ? Reviews.Average(r => r.Rating) : 0;

        public bool IsArchived { get; set; } = false;
    }
}