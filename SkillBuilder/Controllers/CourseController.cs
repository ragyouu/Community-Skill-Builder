using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillBuilder.Data;
using SkillBuilder.Models;
using SkillBuilder.Models.ViewModels;
using SkillBuilder.Services;
using System.Reflection;
using System.Security.Claims;

namespace SkillBuilder.Controllers
{
    [Route("Courses")]
    public class CoursesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IAchievementService _achievementService;

        public CoursesController(AppDbContext context, INotificationService notificationService, IAchievementService achievementService)
        {
            _context = context;
            _notificationService = notificationService;
            _achievementService = achievementService;
        }

        [HttpGet("")]
        public IActionResult CourseCatalog(string? selectedCourse = null, string? search = null)
        {
            ViewData["UseCourseNavbar"] = true;

            var courses = _context.Courses
                .Include(c => c.Artisan)
                .Include(c => c.Enrollments)
                .Include(c => c.Reviews).ThenInclude(r => r.User)
                .Include(c => c.CourseModules).ThenInclude(m => m.Contents)
                .Include(c => c.Materials)
                .AsQueryable();

            var userId = User.FindFirstValue("UserId");
            decimal userThreads = 0;

            if (!string.IsNullOrEmpty(userId))
            {
                var user = _context.Users.FirstOrDefault(u => u.Id == userId);
                if (user != null)
                    userThreads = user.Threads;
            }

            ViewData["UserThreads"] = userThreads;

            // Only show published courses for normal users
            if (!User.IsInRole("Admin"))
            {
                courses = courses.Where(c => c.IsPublished || c.Artisan.UserId == userId);
            }

            if (!string.IsNullOrEmpty(search))
            {
                search = search.ToLower();
                courses = courses.Where(c => c.Title.ToLower().Contains(search));
            }

            var courseList = courses.ToList();

            if (!string.IsNullOrEmpty(selectedCourse))
            {
                var course = courseList.FirstOrDefault(c => c.Link == selectedCourse);
                if (course == null)
                    return NotFound();

                ViewData["ShowCourseDetails"] = true;
                return View("CourseCatalog", new CourseCatalogViewModel
                {
                    Courses = courseList,
                    SelectedCourse = course
                });
            }

            ViewData["ShowCourseDetails"] = false;

            return View("CourseCatalog", new CourseCatalogViewModel
            {
                Courses = courseList,
                SelectedCourse = null
            });
        }

        [HttpGet("RecommendedCourses")]
        public IActionResult RecommendedCourses()
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Get user's interests
            var userInterests = _context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.SelectedInterests)
                .FirstOrDefault();

            // If user has no interests, just show 4 random courses
            if (string.IsNullOrWhiteSpace(userInterests))
            {
                var randomCourses = _context.Courses
                    .OrderBy(c => Guid.NewGuid())
                    .Take(4)
                    .ToList();

                return PartialView("_RecommendedCourses", randomCourses);
            }

            var interestList = userInterests
                .Split(',')
                .Select(i => i.Trim().ToLower())
                .ToList();

            // Step 1: Get courses that match interests (partial matching)
            var matchedCourses = _context.Courses
                .Where(c => c.IsPublished && interestList.Any(interest =>
                    c.Category.ToLower().Contains(interest) ||
                    c.Title.ToLower().Contains(interest)))
                .OrderBy(c => Guid.NewGuid())
                .Take(4)
                .ToList();

            // Step 2: If fewer than 4 matches, fill with random other courses
            if (matchedCourses.Count < 4)
            {
                var remainingCount = 4 - matchedCourses.Count;
                var remainingCourses = _context.Courses
                    .Where(c => !matchedCourses.Select(m => m.Id).Contains(c.Id))
                    .OrderBy(c => Guid.NewGuid())
                    .Take(remainingCount)
                    .ToList();

                matchedCourses.AddRange(remainingCourses);
            }

            return PartialView("_RecommendedCourses", matchedCourses);
        }

        [HttpPost("Enroll")]
        public async Task<IActionResult> Enroll([FromBody] EnrollRequest request)
        {
            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "Login required." });

            var user = await _context.Users
                .Include(u => u.Enrollments)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return NotFound(new { success = false, message = "User not found." });

            if (!user.IsVerified)
                return BadRequest(new { success = false, message = "Please verify your email before enrolling in a course." });

            if (user.IsDeactivated)
                return BadRequest(new { success = false, message = "Your account is deactivated. Please contact support." });

            var course = await _context.Courses
                .Include(c => c.Artisan)
                .FirstOrDefaultAsync(c => c.Id == request.CourseId);

            if (course == null)
                return NotFound(new { success = false, message = "Course not found." });

            if (user.Role == "Artisan" && course.Artisan.UserId == userId)
                return BadRequest(new { success = false, message = "You cannot enroll in your own course." });

            var alreadyEnrolled = user.Enrollments.Any(e => e.CourseId == request.CourseId);
            if (alreadyEnrolled)
                return BadRequest(new { success = false, message = "Already enrolled." });

            // ✅ Deduct threads based on course.DesiredThreads
            decimal threadsToDeduct = course.DesiredThreads ?? 0M;

            if (user.Threads < threadsToDeduct)
                return BadRequest(new { success = false, message = "Not enough threads to enroll in this course." });

            user.Threads -= threadsToDeduct;

            int previousCount = user.Enrollments?.Count() ?? 0;

            _context.Enrollments.Add(new Enrollment
            {
                UserId = userId,
                CourseId = request.CourseId,
                EnrolledAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            int currentCount = _context.Enrollments.Count(e => e.UserId == userId);

            List<string> achievements = new();

            // ✅ Use AchievementService here
            if (previousCount == 0 && currentCount >= 1)
            {
                var firstCourse = await _achievementService.AwardAchievementAsync(userId, "FirstCourseEnrolled");
                if (firstCourse != null) achievements.Add(firstCourse.Title);
            }

            if (previousCount < 3 && currentCount >= 3)
            {
                var threeCourses = await _achievementService.AwardAchievementAsync(userId, "ThreeCoursesEnrolled");
                if (threeCourses != null) achievements.Add(threeCourses.Title);
            }

            // Notifications to course artisan
            if (course.Artisan != null)
            {
                await _notificationService.AddNotificationAsync(
                    course.Artisan.UserId,
                    $"{user.FirstName} enrolled in your course \"{course.Title}\"."
                );
            }

            // Notifications to user
            await _notificationService.AddNotificationAsync(
                userId,
                $"You successfully enrolled in the course \"{course.Title}\"."
            );

            return Json(new
            {
                success = true,
                showAchievement = achievements.Any(),
                achievements,
                threads = user.Threads  // optional: return updated threads
            });
        }

        public class EnrollRequest
        {
            public int CourseId { get; set; }
        }

        [HttpGet("CourseModule/{id}")]
        public IActionResult CourseModule(int id)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return Unauthorized();

            if (user.IsDeactivated)
            {
                TempData["ErrorMessage"] = "Your account is deactivated. You cannot access courses.";
                return RedirectToAction("CourseCatalog");
            }

            // Load course with all related data including materials
            var course = _context.Courses
                .Where(c => c.Id == id)
                .Include(c => c.Materials) // include course materials
                .Include(c => c.CourseModules)
                    .ThenInclude(m => m.Contents)
                        .ThenInclude(content => content.QuizQuestions)
                .Include(c => c.CourseModules)
                    .ThenInclude(m => m.Contents)
                        .ThenInclude(content => content.InteractiveContents)
                .Include(c => c.Artisan)
                .Include(c => c.Enrollments)
                .Include(c => c.Reviews)
                .FirstOrDefault();

            if (course == null) return NotFound();

            // Debugging: log materials count
            Console.WriteLine($"[DEBUG] Course ID: {course.Id}, Materials count: {course.Materials?.Count ?? 0}");

            // 🔒 Prevent access to unpublished courses for normal users
            if (!course.IsPublished && course.Artisan.UserId != userId && !User.IsInRole("Admin"))
            {
                TempData["ErrorMessage"] = "You cannot access this course because it is not published.";
                return RedirectToAction("CourseCatalog");
            }

            var isEnrolled = _context.Enrollments
                .Any(e => e.CourseId == id && e.UserId == userId);

            if (!isEnrolled && course.Artisan.UserId != userId)
            {
                return Forbid();
            }

            // Count all contents in this course
            int totalContents = course.CourseModules.SelectMany(m => m.Contents).Count();

            // Find completed modules for this user
            var completedModules = _context.ModuleProgress
                .Where(mp => mp.UserId == userId && mp.CourseModule.CourseId == course.Id && mp.IsCompleted)
                .Select(mp => mp.CourseModuleId)
                .ToHashSet();

            var orderedModules = course.CourseModules.OrderBy(cm => cm.Order).ToList();

            int completedContents = course.CourseModules
                .Where(m => completedModules.Contains(m.Id))
                .SelectMany(m => m.Contents)
                .Count();

            double progress = totalContents == 0 ? 0 : (double)completedContents / totalContents * 100;
            ViewData["CourseProgress"] = Math.Round(progress, 0);

            var nextModule = orderedModules.FirstOrDefault(m => !completedModules.Contains(m.Id)) ?? orderedModules.Last();
            ViewData["NextModuleId"] = nextModule?.Id;

            // Optional debug: list material titles in log
            if (course.Materials != null && course.Materials.Any())
            {
                foreach (var mat in course.Materials)
                {
                    Console.WriteLine($"[DEBUG] Material: {mat.Title}, FilePath: {mat.FilePath}");
                }
            }
            else
            {
                Console.WriteLine("[DEBUG] No materials found for this course.");
            }

            return View("CourseModules/CourseModule", course);
        }

        private async Task RecalculateProgress(string userId, int courseId)
        {
            var allModuleIds = await _context.CourseModules
                .Where(m => m.CourseId == courseId)
                .Select(m => m.Id)
                .ToListAsync();

            var completedModuleIds = await _context.ModuleProgress
                .Where(mp => mp.UserId == userId && mp.CourseModule.CourseId == courseId && mp.IsCompleted)
                .Select(mp => mp.CourseModuleId)
                .ToListAsync();

            int totalModules = allModuleIds.Count;
            int completedCount = completedModuleIds.Count;

            double progress = totalModules == 0 ? 0 : (double)completedCount / totalModules * 100;

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId);

            // Check if the user has submitted a final project
            var hasSubmittedFinalProject = await _context.CourseProjectSubmissions
                .AnyAsync(s => s.UserId == userId && s.CourseId == courseId &&
                               (s.Status == "Pending" || s.Status == "Approved"));

            // ✅ Only mark course completed if all modules are done AND final project submitted
            if (progress >= 100 && hasSubmittedFinalProject && enrollment != null && !enrollment.IsCompleted)
            {
                enrollment.IsCompleted = true;
                enrollment.CompletedAt = DateTime.UtcNow;

                // ✅ Award 100 threads
                if (enrollment.User != null)
                {
                    enrollment.User.Threads += 100;
                }

                await _context.SaveChangesAsync();
            }
        }

        [HttpPost("SubmitFinalProject")]
        [RequestSizeLimit(200 * 1024 * 1024)]
        public async Task<IActionResult> SubmitFinalProject([FromForm] FinalProjectDto dto)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "Unauthorized" });

            var user = await _context.Users
                .Include(u => u.ProjectSubmissions)
                .FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound(new { success = false, message = "User not found." });

            var course = await _context.Courses
                .Include(c => c.Artisan)
                .FirstOrDefaultAsync(c => c.Id == dto.CourseId);
            if (course == null)
                return NotFound(new { success = false, message = "Course not found." });

            string mediaUrl = null;

            if (dto.File != null && dto.File.Length > 0)
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "projects");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{Guid.NewGuid()}_{dto.File.FileName}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await dto.File.CopyToAsync(stream);
                }

                mediaUrl = $"/uploads/projects/{fileName}";
            }

            var submission = new CourseProjectSubmission
            {
                UserId = userId,
                CourseId = dto.CourseId,
                Title = dto.Title,
                Description = dto.Description,
                MediaUrl = mediaUrl,
                SubmittedAt = DateTime.UtcNow,
                Status = "Pending"
            };

            _context.CourseProjectSubmissions.Add(submission);
            await _context.SaveChangesAsync();

            // 🔄 Recalculate course progress
            await RecalculateProgress(userId, dto.CourseId);

            // 🏆 Award ProjectSubmitted achievement
            var achievements = new List<AchievementViewModel>();

            // First project submission
            var projectAchievement = await _achievementService.AwardAchievementAsync(userId, "ProjectSubmitted");
            if (projectAchievement != null)
                achievements.Add(projectAchievement);

            // Check if course completed achievement should also be awarded
            var completedAchievement = await _achievementService.AwardAchievementAsync(userId, "CourseCompleted");
            if (completedAchievement != null)
                achievements.Add(completedAchievement);

            // 🔔 Notifications to user
            await _notificationService.AddNotificationAsync(
                userId,
                $"Your final project \"{submission.Title}\" has been submitted successfully."
            );

            // 🔔 Notify Artisan
            if (course.Artisan != null)
            {
                await _notificationService.AddNotificationAsync(
                    course.Artisan.UserId,
                    $"A new final project \"{submission.Title}\" has been submitted for your course \"{course.Title}\"."
                );
            }

            return Ok(new
            {
                success = true,
                submissionId = submission.Id,
                achievements, // return all awarded achievements
                threads = user.Threads
            });
        }

        public class FinalProjectDto
        {
            public int CourseId { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public IFormFile File { get; set; }
        }

        [HttpPost("SubmitReview")]
        public async Task<IActionResult> SubmitReview([FromBody] SubmitReviewRequest request)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "Login required." });

            // Check if user is enrolled in the course
            var isEnrolled = _context.Enrollments.Any(e => e.CourseId == request.CourseId && e.UserId == userId);
            if (!isEnrolled)
                return BadRequest(new { success = false, message = "You are not enrolled in this course." });

            var review = new CourseReview
            {
                CourseId = request.CourseId,
                UserId = userId,
                Rating = request.Rating,
                Comment = request.Comment ?? "",
                CreatedAt = DateTime.UtcNow
            };

            _context.CourseReviews.Add(review);
            await _context.SaveChangesAsync();

            // ✅ Award 10 threads only once per user
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user != null)
            {
                bool firstReview = !_context.CourseReviews.Any(r => r.UserId == userId && r.Id != review.Id);
                if (firstReview)
                {
                    user.Threads += 10; // give 10 threads
                    await _context.SaveChangesAsync();
                }
            }

            // Recalculate average rating
            var reviews = _context.CourseReviews.Where(r => r.CourseId == request.CourseId);
            var average = reviews.Any() ? reviews.Average(r => r.Rating) : 0;
            var totalReviews = reviews.Count();

            var course = _context.Courses.Include(c => c.Artisan).FirstOrDefault(c => c.Id == request.CourseId);
            if (course != null && course.Artisan != null)
            {
                await _notificationService.AddNotificationAsync(
                    course.Artisan.UserId,
                    $"{User.Identity.Name} submitted a review for your course \"{course.Title}\"."
                );
            }

            await _notificationService.AddNotificationAsync(
                userId,
                $"You submitted a review for the course \"{course?.Title}\"."
            );

            return Ok(new
            {
                success = true,
                averageRating = average,
                totalReviews
            });
        }

        public class SubmitReviewRequest
        {
            public int CourseId { get; set; }
            public int Rating { get; set; }
            public string? Comment { get; set; }
        }

        [HttpPost("UpdateProgress")]
        public async Task<IActionResult> UpdateProgress([FromBody] ProgressUpdateModel model)
        {
            var userId = User.FindFirst("UserId")?.Value;
            if (userId == null)
                return Unauthorized();

            foreach (var moduleIndex in model.CompletedModules)
            {
                var courseModule = await _context.CourseModules
                    .FirstOrDefaultAsync(m => m.CourseId == model.CourseId && m.Order == moduleIndex);

                if (courseModule == null) continue;

                var existing = await _context.ModuleProgress
                    .FirstOrDefaultAsync(p => p.UserId == userId && p.CourseModuleId == courseModule.Id);

                if (existing == null)
                {
                    _context.ModuleProgress.Add(new ModuleProgress
                    {
                        UserId = userId,
                        CourseModuleId = courseModule.Id,
                        IsCompleted = true,
                        CompletedAt = DateTime.UtcNow,
                        LastUpdated = DateTime.UtcNow
                    });
                }
                else
                {
                    existing.IsCompleted = true;
                    existing.LastUpdated = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();

            // Recalculate overall progress (shared)
            await RecalculateProgress(userId, model.CourseId);

            return Ok();
        }

        [HttpPost("ResetProgress")]
        public IActionResult ResetProgress([FromBody] ProgressUpdateModel model)
        {
            var userId = User.FindFirstValue("UserId");

            // Remove module progress
            var toDelete = _context.ModuleProgress
                .Where(p => p.UserId == userId &&
                            _context.CourseModules
                                    .Where(m => m.CourseId == model.CourseId)
                                    .Select(m => m.Id)
                                    .Contains(p.CourseModuleId))
                .ToList();

            _context.ModuleProgress.RemoveRange(toDelete);

            // Reset enrollment completion
            var enrollment = _context.Enrollments
                .FirstOrDefault(e => e.UserId == userId && e.CourseId == model.CourseId);

            if (enrollment != null)
            {
                enrollment.IsCompleted = false;
                enrollment.CompletedAt = null;
            }

            var submissions = _context.CourseProjectSubmissions
                .Where(s => s.UserId == userId && s.CourseId == model.CourseId)
                .ToList();

            _context.CourseProjectSubmissions.RemoveRange(submissions);

            _context.SaveChanges();

            return Ok(new { message = "Reset successful" });
        }

        [HttpPost("SaveUserPoints")]
        public async Task<IActionResult> SaveUserPoints([FromBody] SavePointsRequest request)
        {
            try
            {
                // Check if user is authenticated
                var userId = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(userId))
                {
                    Console.WriteLine("⚠️ SaveUserPoints: No user ID found (unauthenticated request).");
                    return Unauthorized(new { success = false, message = "User not logged in." });
                }

                // Find user
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    Console.WriteLine($"⚠️ SaveUserPoints: User not found for ID {userId}");
                    return NotFound(new { success = false, message = "User not found." });
                }

                // Add points safely
                user.Points += request.Points;
                await _context.SaveChangesAsync();

                Console.WriteLine($"✅ SaveUserPoints: Added {request.Points} points for user {user.Email}. New total: {user.Points}");

                return Ok(new
                {
                    success = true,
                    totalPoints = user.Points
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ SaveUserPoints error: {ex.Message}\n{ex.StackTrace}");
                return StatusCode(500, new { success = false, message = "Internal server error while saving points." });
            }
        }

        public class SavePointsRequest
        {
            public int Points { get; set; }
        }

        [HttpDelete("Delete/{id}")]
        [Authorize(Roles = "Artisan")]
        public async Task<IActionResult> DeleteCourse(int id)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var course = await _context.Courses
                .Include(c => c.Artisan)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (course == null)
                return Json(new { success = false, message = "Course not found." });

            if (course.Artisan?.UserId != userId)
                return Forbid();

            // 🗂️ Soft delete (archive)
            course.IsArchived = true;
            await _context.SaveChangesAsync();

            // ✅ Send notification to the artisan confirming success
            await _notificationService.AddNotificationAsync(
                userId,
                $"Your course \"{course.Title}\" has been deleted successfully."
            );

            // ✅ Return redirect URL with artisan ID (use Artisan.Id or Artisan.ArtisanId depending on your model)
            var artisanId = course.Artisan.ArtisanId;
            return Json(new
            {
                success = true,
                message = "Course archived successfully.",
                redirectUrl = $"/ArtisanProfile/{artisanId}"
            });
        }

        public class ReportCourseViewModel
        {
            public int CourseId { get; set; }
            public string Reason { get; set; } = string.Empty;
            public string? Details { get; set; }
        }

        [HttpPost("Report/{id}")]
        public async Task<IActionResult> ReportCourse(int id, [FromBody] ReportCourseViewModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "Invalid report data." });

            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var course = await _context.Courses
                .Include(c => c.Artisan)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (course == null)
                return NotFound(new { success = false, message = "Course not found." });

            // 📝 Save report
            var report = new CourseReport
            {
                CourseId = course.Id,
                ReporterId = userId,
                Reason = model.Reason,
                Details = model.Details,
                ReportedAt = DateTime.UtcNow
            };

            _context.CourseReports.Add(report);
            await _context.SaveChangesAsync();

            // 🔔 Notify reporting user
            await _notificationService.AddNotificationAsync(
                userId,
                $"⚠️ You successfully reported the course \"{course.Title}\". Reason: \"{model.Reason}\"."
            );

            // 🔔 Notify all admins
            var adminIds = await _context.Users
                .Where(u => u.Role == "Admin")
                .Select(u => u.Id)
                .ToListAsync();

            foreach (var adminId in adminIds)
            {
                await _notificationService.AddNotificationAsync(
                    adminId,
                    $"⚠️ Course \"{course.Title}\" (by {course.Artisan?.FirstName} {course.Artisan?.LastName}) has been reported. Reason: {model.Reason}"
                );
            }

            return Ok(new { success = true, message = "Course reported successfully." });
        }

        [HttpGet("ViewCourseModule/{id}")]
        public IActionResult ViewCourseModule(int id)
        {
            var course = _context.Courses
                .Include(c => c.CourseModules)
                    .ThenInclude(m => m.Contents)
                        .ThenInclude(mc => mc.QuizQuestions)
                .Include(c => c.CourseModules)
                    .ThenInclude(m => m.Contents)
                        .ThenInclude(mc => mc.InteractiveContents) // ✅ include interactive contents
                .Include(c => c.Artisan)
                .FirstOrDefault(c => c.Id == id);

            if (course == null) return NotFound();

            // Build module DTOs
            var modules = course.CourseModules
                .OrderBy(m => m.Order)
                .Select(m => new ModuleJson
                {
                    Id = m.Id,
                    Title = m.Title,
                    Order = m.Order,
                    Lessons = m.Contents
                        .OrderBy(c => c.Order)
                        .Select(l => new LessonJson
                        {
                            Id = l.Id,
                            Title = l.Title ?? "",
                            ContentType = l.ContentType ?? "Text",
                            ContentText = l.ContentText ?? "",
                            MediaUrl = l.MediaUrl ?? "",
                            Order = l.Order,
                            QuizQuestions = l.QuizQuestions
                                .Select(q => new QuizQuestionJson
                                {
                                    Id = q.Id,
                                    Question = q.Question ?? "",
                                    Options = new string[]
                                    {
                                q.OptionA ?? "",
                                q.OptionB ?? "",
                                q.OptionC ?? "",
                                q.OptionD ?? ""
                                    },
                                    CorrectAnswer = q.CorrectAnswer ?? ""
                                }).ToList(),
                            InteractiveContents = l.InteractiveContents
                                .Select(ic => new InteractiveContentJson
                                {
                                    Id = ic.Id,
                                    ContentType = ic.ContentType ?? "Text",
                                    ContentText = ic.ContentText ?? "",
                                    OptionA = ic.OptionA,
                                    OptionB = ic.OptionB,
                                    OptionC = ic.OptionC,
                                    OptionD = ic.OptionD,
                                    CorrectAnswer = ic.CorrectAnswer
                                }).ToList()
                        }).ToList()
                }).ToList();

            var finalProject = new FinalProjectJson
            {
                Title = course.FinalProjectTitle ?? "",
                Description = course.FinalProjectDescription ?? ""
            };

            // Map to view model
            var model = new CourseDetailsViewModel
            {
                Id = course.Id,
                Title = course.Title ?? "",
                FinalProjectTitle = finalProject?.Title ?? "",
                FinalProjectDescription = finalProject?.Description ?? "",
                Modules = modules
                    .Select(m => new CourseModuleViewModel
                    {
                        Title = m.Title ?? "",
                        Lessons = (m.Lessons ?? new List<LessonJson>())
                            .Select(l => new LessonViewModel
                            {
                                Id = l.Id,
                                Title = l.Title ?? "",
                                LessonType = l.ContentType ?? "Text",
                                ContentText = l.ContentText ?? "",
                                VideoUrl = l.ContentType == "Video + Text" ? l.MediaUrl : null,
                                ImageUrl = l.ContentType == "Image + Text" ? l.MediaUrl : null,
                                QuizQuestions = (l.QuizQuestions ?? new List<QuizQuestionJson>())
                                    .Select(q => new QuizQuestionViewModel
                                    {
                                        QuestionText = q.Question ?? "",
                                        OptionA = q.Options.Length > 0 ? q.Options[0] : "",
                                        OptionB = q.Options.Length > 1 ? q.Options[1] : "",
                                        OptionC = q.Options.Length > 2 ? q.Options[2] : "",
                                        OptionD = q.Options.Length > 3 ? q.Options[3] : "",
                                        CorrectAnswer = q.CorrectAnswer ?? ""
                                    }).ToList(),
                                InteractiveContents = (l.InteractiveContents ?? new List<InteractiveContentJson>())
                                    .Select(ic => new InteractiveContentViewModel
                                    {
                                        Id = ic.Id,
                                        ContentType = ic.ContentType ?? "Text",
                                        ContentText = ic.ContentText ?? "",
                                        OptionA = ic.OptionA,
                                        OptionB = ic.OptionB,
                                        OptionC = ic.OptionC,
                                        OptionD = ic.OptionD,
                                        CorrectAnswer = ic.CorrectAnswer
                                    }).ToList()
                            }).ToList()
                    }).ToList()
            };

            return View("~/Views/Shared/Sections/_CourseModuleViewing.cshtml", model);
        }

        // Updated DTOs / helper classes
        public class LessonJson
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";
            public string ContentType { get; set; } = "Text";
            public string ContentText { get; set; } = "";
            public string MediaUrl { get; set; } = "";
            public int Order { get; set; }
            public List<QuizQuestionJson> QuizQuestions { get; set; } = new();
            public List<InteractiveContentJson> InteractiveContents { get; set; } = new(); // ✅ added
        }

        public class InteractiveContentJson
        {
            public int Id { get; set; }
            public string ContentType { get; set; } = "Text";
            public string ContentText { get; set; } = "";
            public string? OptionA { get; set; }
            public string? OptionB { get; set; }
            public string? OptionC { get; set; }
            public string? OptionD { get; set; }
            public string? CorrectAnswer { get; set; }
        }

        public class QuizQuestionJson
        {
            public int Id { get; set; }
            public string Question { get; set; } = "";
            public string[] Options { get; set; } = new string[4];
            public string CorrectAnswer { get; set; } = "";
        }

        public class ModuleJson
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";
            public int Order { get; set; }
            public List<LessonJson> Lessons { get; set; } = new();
        }

        public class FinalProjectJson
        {
            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
        }

        // GET: /Courses/Forum/5
        [HttpGet("Forum/{courseId}")]
        public async Task<IActionResult> Forum(int courseId)
        {
            var course = await _context.Courses.FindAsync(courseId);
            if (course == null) return NotFound();

            var posts = await _context.CourseForumPosts
                .Where(p => p.CourseId == courseId)
                .Include(p => p.User)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            ViewBag.CourseTitle = course.Title;
            ViewBag.CourseId = courseId;

            return View("~/Views/Shared/Sections/_CourseForum.cshtml", posts);
        }

        public class ForumPostRequest
        {
            public string Content { get; set; }
        }

        [HttpPost("Forum/{courseId}")]
        public async Task<IActionResult> AddForumPost(int courseId, [FromBody] ForumPostRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Content))
                return BadRequest("Content is empty");

            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var post = new CourseForumPost
            {
                CourseId = courseId,
                UserId = userId,
                Content = request.Content,
                CreatedAt = DateTime.UtcNow
            };

            _context.CourseForumPosts.Add(post);
            await _context.SaveChangesAsync();

            // Include User navigation to render name in partial view
            await _context.Entry(post).Reference(p => p.User).LoadAsync();

            return Json(new
            {
                id = post.Id,
                content = post.Content,
                createdAt = post.CreatedAt.ToString("MMM dd, yyyy"),
                userName = post.User.FirstName + " " + post.User.LastName,
                userRole = post.User.Role
            });
        }

        [HttpPost("UploadForumMedia")]
        [AllowAnonymous] // Optional: allow anonymous uploads too
        public async Task<IActionResult> UploadForumMedia(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { location = "" });

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "uploads");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{Guid.NewGuid()}_{file.FileName}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var fileUrl = $"/assets/uploads/{fileName}";
            return Json(new { location = fileUrl });
        }

        public class EditForumPostRequest
        {
            public string Content { get; set; } = "";
        }

        [HttpPut("Forum/Edit/{postId}")]
        public async Task<IActionResult> EditForumPost(int postId, [FromBody] ForumPostRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Content))
                return BadRequest(new { success = false, message = "Content cannot be empty." });

            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var post = await _context.CourseForumPosts
                .FirstOrDefaultAsync(p => p.Id == postId);

            if (post == null)
                return NotFound(new { success = false, message = "Post not found." });

            if (post.UserId != userId)
                return Forbid();

            post.Content = request.Content;
            post.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                postId = post.Id,
                content = post.Content,
                updatedAt = post.UpdatedAt?.ToString("MMM dd, yyyy HH:mm")
            });
        }

        [HttpDelete("Forum/Delete/{postId}")]
        public async Task<IActionResult> DeleteForumPost(int postId)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var post = await _context.CourseForumPosts
                .FirstOrDefaultAsync(p => p.Id == postId);

            if (post == null)
                return NotFound(new { success = false, message = "Post not found." });

            if (post.UserId != userId)
                return Forbid();

            _context.CourseForumPosts.Remove(post);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Post deleted successfully." });
        }
    }
}