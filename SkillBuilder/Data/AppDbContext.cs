using Microsoft.EntityFrameworkCore;
using SkillBuilder.Models;
using SkillBuilder.Models.Entities;
using System.Text.Json;

namespace SkillBuilder.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<UserReport> UserReports { get; set; }
        public DbSet<UserAchievement> UserAchievements { get; set; }
        public DbSet<CourseProjectSubmission> CourseProjectSubmissions { get; set; }
        public DbSet<Artisan> Artisans { get; set; }
        public DbSet<ArtisanApplication> ArtisanApplications { get; set; }
        public DbSet<ArtisanWork> ArtisanWorks { get; set; }
        public DbSet<Admin> Admins { get; set; }
        public DbSet<AboutFeature> AboutFeatures { get; set; }
        public DbSet<Course> Courses { get; set; }
        public DbSet<CourseForumPost> CourseForumPosts { get; set; }
        public DbSet<Enrollment> Enrollments { get; set; }
        public DbSet<CourseModule> CourseModules { get; set; }
        public DbSet<CourseMaterial> CourseMaterials { get; set; }
        public DbSet<ModuleContent> ModuleContents { get; set; }
        public DbSet<InteractiveContent> InteractiveContents { get; set; }
        public DbSet<QuizQuestion> QuizQuestions { get; set; }
        public DbSet<ModuleProgress> ModuleProgress { get; set; }
        public DbSet<SupportSessionRequest> SupportSessionRequests { get; set; }
        public DbSet<CourseReview> CourseReviews { get; set; }
        public DbSet<CourseReport> CourseReports { get; set; }
        public DbSet<CommunityTestimonial> CommunityTestimonials { get; set; }
        public DbSet<CommunityHighlight> CommunityHighlights { get; set; }
        public DbSet<CommunityPost> CommunityPosts { get; set; }
        public DbSet<CommunityPostReport> CommunityPostReports { get; set; }
        public DbSet<Community> Communities { get; set; }
        public DbSet<CommunityReport> CommunityReports { get; set; }
        public DbSet<CommunityJoinRequest> CommunityJoinRequests { get; set; }
        public DbSet<CommunityMembership> CommunityMemberships { get; set; }
        public DbSet<Notification> Notifications { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ✅ Force all DateTime fields to use UTC
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime))
                    {
                        property.SetValueConverter(
                            new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                                v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
                                v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
                            )
                        );
                    }
                    else if (property.ClrType == typeof(DateTime?))
                    {
                        property.SetValueConverter(
                            new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
                                v => v == null ? null : (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()),
                                v => v == null ? null : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
                            )
                        );
                    }
                }
            }

            modelBuilder.Entity<Course>().HasQueryFilter(c => !c.IsArchived);
            modelBuilder.Entity<Community>().HasQueryFilter(c => !c.IsArchived);
            modelBuilder.Entity<User>().HasQueryFilter(u => !u.IsArchived);

            // User default value for Avatar
            modelBuilder.Entity<User>()
                .Property(u => u.UserAvatar)
                .HasDefaultValue("/assets/Avatar/Sample10.svg");


            // About Features
            modelBuilder.Entity<AboutFeature>().HasData(
                new AboutFeature { Id = 001, IconPath = "/assets/Icons/Course.ico", Title = "Structured Course", Description = "Detailed learning paths from beginner to professional levels in traditional and contemporary art forms." },
                new AboutFeature { Id = 002, IconPath = "/assets/Icons/Community.ico", Title = "Community Engagement", Description = "Share insights, feedback, and experiences with fellow learners and master artisans." },
                new AboutFeature { Id = 003, IconPath = "/assets/Icons/Sessions.ico", Title = "Live Sessions", Description = "Scheduled real-time query sessions with course instructor for personalized guidance." },
                new AboutFeature { Id = 004, IconPath = "/assets/Icons/Download.ico", Title = "Offline Access", Description = "Download courses for offline learning, ensuring accessibility regardless of internet connectivity." }
            );

            // Community Testimonials
            modelBuilder.Entity<CommunityTestimonial>().HasData(
                new CommunityTestimonial
                {
                    Id = 0000001,
                    Comment = "Our platform addresses the urgent need to preserve Philippine cultural and traditional art skills that are at risk of disappearing due to modernization.",
                    AvatarPath = "/assets/Avatar/Sample1.ico",
                    UserName = "Maria Santos",
                    Role = "Learner"
                },
                new CommunityTestimonial
                {
                    Id = 0000002,
                    Comment = "Lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt. Lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor.",
                    AvatarPath = "/assets/Avatar/Sample2.ico",
                    UserName = "Denise Velasco",
                    Role = "Researcher"
                },
                new CommunityTestimonial
                {
                    Id = 0000003,
                    Comment = "Lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt.",
                    AvatarPath = "/assets/Avatar/Sample3.ico",
                    UserName = "Pamela Cruz",
                    Role = "Artisan"
                },
                new CommunityTestimonial
                {
                    Id = 0000004,
                    Comment = "Lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor aaa aaa aaa  incididunt lorem ipsum dolor sit.",
                    AvatarPath = "/assets/Avatar/Sample4.ico",
                    UserName = "Angela Tiz",
                    Role = "Artisan"
                },
                new CommunityTestimonial
                {
                    Id = 0000005,
                    Comment = "Lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt. Lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor.",
                    AvatarPath = "/assets/Avatar/Sample5.ico",
                    UserName = "Marlene Qul",
                    Role = "Artisan"
                },
                new CommunityTestimonial
                {
                    Id = 0000006,
                    Comment = "Lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur.",
                    AvatarPath = "/assets/Avatar/Sample6.ico",
                    UserName = "Brad Kiminda",
                    Role = "Artisan"
                },
                new CommunityTestimonial
                {
                    Id = 0000007,
                    Comment = "Lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt. Lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor.",
                    AvatarPath = "/assets/Avatar/Sample7.ico",
                    UserName = "Michael Ramirez",
                    Role = "Artisan"
                },
                new CommunityTestimonial
                {
                    Id = 0000008,
                    Comment = "Lorem ipsum dolor sit amet, consectetur on aa aa aa aa adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod.",
                    AvatarPath = "/assets/Avatar/Sample8.ico",
                    UserName = "Ella Parilla",
                    Role = "Artisan"
                },
                new CommunityTestimonial
                {
                    Id = 0000009,
                    Comment = "Lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem .   aaa aaa aa aaa aa aa aa aa  ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt. Lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor.",
                    AvatarPath = "/assets/Avatar/Sample9.ico",
                    UserName = "James Dawg",
                    Role = "Artisan"
                }
            );

            // Community Highlights
            modelBuilder.Entity<CommunityHighlight>().HasData(
                new CommunityHighlight
                {
                    Id = 0000001,
                    Name = "Maria Santos",
                    Role = "Learner",
                    Avatar = "/assets/Avatar/Sample1.ico",
                    Comment = "Lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet.",
                    Image = "/assets/Community Pics/Pottery.png",
                    Comments = 36
                },
                new CommunityHighlight
                {
                    Id = 0000002,
                    Name = "James dela Cruz",
                    Role = "Artisan",
                    Avatar = "/assets/Avatar/Sample9.ico",
                    Comment = "Lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet.",
                    Image = "/assets/Community Pics/Weaving.png",
                    Comments = 18
                },
                new CommunityHighlight
                {
                    Id = 0000003,
                    Name = "Kim Navarro",
                    Role = "Researcher",
                    Avatar = "/assets/Avatar/Sample5.ico",
                    Comment = "Lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet, consectetur on adipiscing elit, eiusmod tempor incididunt lorem ipsum dolor sit amet.",
                    Image = "/assets/Community Pics/Woodcarving.png",
                    Comments = 41
                }
            );

            // USER ⇄ ARTISAN (1:1)
            modelBuilder.Entity<User>()
                .HasOne(u => u.Artisan)
                .WithOne(a => a.User)
                .HasForeignKey<Artisan>(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // ARTISAN ⇄ ARTISAN WORKS (1:M)
            modelBuilder.Entity<ArtisanWork>()
                .HasOne(w => w.Artisan)
                .WithMany(a => a.Works)
                .HasForeignKey(w => w.ArtisanId)
                .OnDelete(DeleteBehavior.Cascade);

            // COURSE ⇄ ARTISAN (1:M)
            modelBuilder.Entity<Course>()
                .HasOne(c => c.Artisan)
                .WithMany(a => a.Courses)
                .HasForeignKey(c => c.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // ENROLLMENT ⇄ USER (M:1)
            modelBuilder.Entity<Enrollment>()
                .HasOne(e => e.User)
                .WithMany(u => u.Enrollments)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // ENROLLMENT ⇄ COURSE (M:1)
            modelBuilder.Entity<Enrollment>()
                .HasOne(e => e.Course)
                .WithMany(c => c.Enrollments)
                .HasForeignKey(e => e.CourseId)
                .OnDelete(DeleteBehavior.Restrict);

            // COURSE REVIEW ⇄ COURSE (M:1)
            modelBuilder.Entity<CourseReview>()
                .HasOne(r => r.User)
                .WithMany(u => u.Reviews)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // COURSE REVIEW ⇄ USER (M:1)
            modelBuilder.Entity<CourseReview>()
                .HasOne(r => r.Course)
                .WithMany(c => c.Reviews)
                .HasForeignKey(r => r.CourseId)
                .OnDelete(DeleteBehavior.Restrict);

            // COURSE SUBMISSION ⇄ USER (M:1)
            modelBuilder.Entity<CourseProjectSubmission>()
                .HasOne(p => p.User)
                .WithMany(u => u.ProjectSubmissions)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // COURSE SUBMISSION ⇄ COURSE (M:1)
            modelBuilder.Entity<CourseProjectSubmission>()
                .HasOne(p => p.Course)
                .WithMany(c => c.ProjectSubmissions)
                .HasForeignKey(p => p.CourseId)
                .OnDelete(DeleteBehavior.Restrict);

            // COURSE MODULE ⇄ COURSE (M:1)
            modelBuilder.Entity<CourseModule>()
                .HasOne(cm => cm.Course)
                .WithMany(c => c.CourseModules)
                .HasForeignKey(cm => cm.CourseId);

            // COURSE MATERIAL ⇄ COURSE (M:1)
            modelBuilder.Entity<CourseMaterial>()
                .HasOne(cm => cm.Course)
                .WithMany(c => c.Materials)
                .HasForeignKey(cm => cm.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CommunityPost>()
                .HasOne(p => p.Author)
                .WithMany()   // or .WithMany(u => u.Posts) if you track them
                .HasForeignKey(p => p.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CommunityMembership>()
                .HasOne(cm => cm.User)
                .WithMany(u => u.Memberships)
                .HasForeignKey(cm => cm.UserId)
                .OnDelete(DeleteBehavior.Restrict);  // prevents cascade cycles

            modelBuilder.Entity<CommunityMembership>()
                .HasOne(cm => cm.Community)
                .WithMany(c => c.Memberships)
                .HasForeignKey(cm => cm.CommunityId)
                .OnDelete(DeleteBehavior.Cascade);

            // Community -> Creator (User) : restrict deletion
            modelBuilder.Entity<Community>()
                .HasOne(c => c.Creator)
                .WithMany()
                .HasForeignKey(c => c.CreatorId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade

            // CommunityJoinRequest -> User : restrict deletion
            modelBuilder.Entity<CommunityJoinRequest>()
                .HasOne(cjr => cjr.User)
                .WithMany(u => u.CommunityJoinRequests)
                .HasForeignKey(cjr => cjr.UserId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade

            // CommunityJoinRequest -> Community : optional cascade
            modelBuilder.Entity<CommunityJoinRequest>()
                .HasOne(cjr => cjr.Community)
                .WithMany(c => c.JoinRequests)
                .HasForeignKey(cjr => cjr.CommunityId)
                .OnDelete(DeleteBehavior.Restrict);

            // MODULE PROGRESS ⇄ COURSE MODULE (M:1)
            modelBuilder.Entity<ModuleProgress>()
                .HasOne(mp => mp.CourseModule)
                .WithMany(cm => cm.Progresses)
                .HasForeignKey(mp => mp.CourseModuleId);

            // QUIZ QUESTION ⇄ COURSE MODULE (M:1)
            modelBuilder.Entity<QuizQuestion>()
                .HasOne(q => q.ModuleContent)
                .WithMany(mc => mc.QuizQuestions)
                .HasForeignKey(q => q.ModuleContentId)
                .OnDelete(DeleteBehavior.Cascade);

        }
    }
}
