using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillBuilder.Data;
using SkillBuilder.Models;
using SkillBuilder.Models.ViewModels;
using SkillBuilder.Services;

namespace SkillBuilder.Controllers
{
    [Authorize(AuthenticationSchemes = "TahiAuth", Roles = "Learner")]
    [Route("UserProfile")]
    public class UserProfileController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notificationService;
        private readonly ICloudinaryService _cloudinaryService;

        public UserProfileController(AppDbContext context, IEmailService emailService, INotificationService notificationService, ICloudinaryService cloudinaryService)
        {
            _context = context;
            _emailService = emailService;
            _notificationService = notificationService;
            _cloudinaryService = cloudinaryService;
        }

        [HttpGet("{id}")]
        public IActionResult UserProfile(string id, int? communityId)
        {
            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (id != currentUserId)
                return Forbid();

            var user = _context.Users
                .Include(u => u.Artisan)
                .Include(u => u.Enrollments)
                    .ThenInclude(e => e.Course)
                .Include(u => u.Reviews)
                .FirstOrDefault(u => u.Id == id);

            if (user == null) return NotFound();

            var moduleProgress = _context.ModuleProgress
                .Include(mp => mp.CourseModule)
                .Where(mp => mp.UserId == user.Id)
                .ToList();

            var enrolledCourses = user.Enrollments?.Select(e => e.Course).ToList() ?? new List<Course>();

            var inProgressCourses = user.Enrollments
                .Where(e => !e.IsCompleted) // only include courses that are not completed
                .Select(e => new CourseProgressViewModel
                {
                    UserId = user.Id,
                    CourseId = e.CourseId,
                    CourseTitle = e.Course.Title,
                    CourseDescription = e.Course.Overview,
                    IsPublished = e.Course.IsPublished,
                    IsCompleted = e.IsCompleted // directly from enrollment
                })
                .ToList();

            var allCourses = _context.Courses
                .Include(c => c.Artisan)
                .ToList();

            var submittedProjects = _context.CourseProjectSubmissions
                .Include(p => p.Course)
                .Where(p => p.UserId == user.Id)
                .ToList();

            var supportRequests = _context.SupportSessionRequests
                .Where(r => r.UserId == user.Id)
                .Include(r => r.Course)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            List<SupportSessionRequest> artisanSupportRequests = new List<SupportSessionRequest>();
            if (user.Artisan != null)
            {
                var artisanId = user.Artisan.ArtisanId;
                artisanSupportRequests = _context.SupportSessionRequests
                    .Include(r => r.User)
                    .Include(r => r.Course)
                    .Where(r => r.Course.CreatedBy == artisanId)
                    .OrderByDescending(r => r.CreatedAt)
                    .ToList();
            }

            var achievements = GetAchievementsForUser(user);

            var userInterests = user.SelectedInterests?
                .Split(',')
                .Select(i => i.Trim())
                .ToList() ?? new List<string>();

            var recommendedCourses = _context.Courses
                .Where(c => userInterests.Contains(c.Category))
                .OrderBy(c => Guid.NewGuid())
                .Take(4)
                .ToList();

            var leaderboardUsers = _context.Users
                .OrderByDescending(u => u.Points)
                .Take(20)
                .ToList();

            var artisans = _context.Artisans
                .Include(a => a.User)
                .Where(a => !a.IsArchived && a.User != null)
                .ToList();

            var userCommunities = _context.Communities
                .Where(c => c.CreatorId == user.Id && !c.IsArchived && c.IsPublished)
                .Select(c => new CommunitiesViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    Description = c.Description ?? "",
                    AvatarUrl = c.AvatarUrl ?? "/assets/placeholder-avatar.png",
                    CoverImageUrl = c.CoverImageUrl ?? "/assets/placeholder-community-cover.jpg",
                    MembersCount = c.MembersCount,
                    CreatorId = c.CreatorId
                })
                .ToList();

            var joinedCommunities = _context.CommunityMemberships
                .Include(m => m.Community)
                .Where(m => m.UserId == user.Id && !m.Community.IsArchived && m.Community.IsPublished)
                .Select(m => new CommunitiesViewModel
                {
                    Id = m.Community.Id,
                    Name = m.Community.Name,
                    Description = m.Community.Description ?? "",
                    AvatarUrl = m.Community.AvatarUrl ?? "/assets/placeholder-avatar.png",
                    CoverImageUrl = m.Community.CoverImageUrl ?? "/assets/placeholder-community-cover.jpg",
                    MembersCount = m.Community.MembersCount,
                    CreatorId = m.Community.CreatorId
                })
                .ToList();

            var communityMembers = new Dictionary<int, List<CommunityMemberViewModel>>();

            foreach (var community in userCommunities)
            {
                var members = _context.CommunityMemberships
                    .Include(m => m.User)
                    .Where(m => m.CommunityId == community.Id && m.User != null && !m.User.IsArchived)
                    .Select(m => new CommunityMemberViewModel
                    {
                        UserId = m.User.Id,
                        FullName = m.User.FirstName + " " + m.User.LastName,
                        Email = m.User.Email,
                        AvatarUrl = string.IsNullOrEmpty(m.User.UserAvatar) ? "/assets/default-avatar.png" : m.User.UserAvatar,
                        Role = string.IsNullOrEmpty(m.Role) ? "Member" : m.Role,
                        JoinedAt = m.JoinedAt,
                        CommunityId = m.CommunityId
                    })
                    .ToList();

                communityMembers[community.Id] = members;
            }

            var communityIds = userCommunities.Select(c => c.Id).ToList();

            var pendingJoinRequests = _context.CommunityJoinRequests
                .Where(r => communityIds.Contains(r.CommunityId) && r.Status == "Pending")
                .Include(r => r.User) // get requester info
                .ToList();

            var viewModel = new UserProfileViewModel
            {
                User = user,
                EnrolledCourses = enrolledCourses,
                AllCourses = allCourses,
                SubmittedProjects = submittedProjects,
                Achievements = achievements,
                CourseProgresses = inProgressCourses,
                SupportRequests = supportRequests,
                ArtisanSupportRequests = artisanSupportRequests,
                RecommendedCourses = recommendedCourses,
                LeaderboardUsers = leaderboardUsers,
                Artisans = artisans,
                MyCommunities = userCommunities,
                JoinedCommunities = joinedCommunities,
                PendingJoinRequests = pendingJoinRequests,
                CommunityMembers = communityMembers,
                SelectedCommunityId = communityId ?? userCommunities.FirstOrDefault()?.Id
            };

            return View("~/Views/Profile/UserProfile.cshtml", viewModel);
        }

        [HttpPost("SaveInterests")]
        public async Task<IActionResult> SaveInterests([FromBody] List<string> interests)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized("User not found.");

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound("User not found.");

            // Save interests (empty allowed)
            user.SelectedInterests = interests != null && interests.Count > 0
                ? string.Join(",", interests)
                : null;

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Interests saved successfully." });
        }

        [HttpPost("/api/UserProfile/UploadActivity")]
        public async Task<IActionResult> UploadActivity([FromForm] string courseId, [FromForm] IFormFile activityImage)
        {
            if (activityImage == null || activityImage.Length == 0)
                return BadRequest("No image uploaded.");

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (!int.TryParse(courseId, out int parsedCourseId))
                return BadRequest("Invalid course ID.");

            var course = await _context.Courses.FindAsync(parsedCourseId);
            if (course == null) return NotFound("Course not found.");

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/projects");
            Directory.CreateDirectory(uploadsFolder);
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(activityImage.FileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await activityImage.CopyToAsync(stream);
            }

            var newSubmission = new CourseProjectSubmission
            {
                UserId = userId,
                CourseId = course.Id,
                MediaUrl = $"/uploads/projects/{fileName}",
                SubmittedAt = DateTime.Now
            };

            _context.CourseProjectSubmissions.Add(newSubmission);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Activity submitted!", imageUrl = newSubmission.MediaUrl });
        }

        [HttpGet("")]
        public IActionResult Index()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            return Redirect($"/UserProfile/{userId}");
        }

        private List<AchievementViewModel> GetAchievementsForUser(User user)
        {
            var achievements = new List<AchievementViewModel>();

            achievements.Add(new AchievementViewModel
            {
                AchievementKey = "FirstCourseEnrolled",
                Title = "First Course Enrolled",
                Condition = "Enroll in your first course",
                IsAchieved = user.Enrollments != null && user.Enrollments.Any()
            });

            achievements.Add(new AchievementViewModel
            {
                AchievementKey = "ThreeCoursesEnrolled",
                Title = "Lifelong Learner",
                Condition = "Enroll in 3 courses",
                IsAchieved = user.Enrollments != null && user.Enrollments.Count() >= 3
            });

            achievements.Add(new AchievementViewModel
            {
                AchievementKey = "CourseCompleted",
                Title = "Course Completed",
                Condition = "Complete your first course",
                IsAchieved = user.Enrollments != null && user.Enrollments.Any(e => e.IsCompleted)
            });

            achievements.Add(new AchievementViewModel
            {
                AchievementKey = "ProjectSubmitted",
                Title = "Project Submitted",
                Condition = "Submit your first project",
                IsAchieved = user.ProjectSubmissions != null && user.ProjectSubmissions.Any()
            });

            achievements.Add(new AchievementViewModel
            {
                AchievementKey = "FirstReviewSubmitted",
                Title = "First Review Submitted",
                Condition = "Submit your first review",
                IsAchieved = user.Reviews != null && user.Reviews.Any()
            });

            return achievements;
        }


        [HttpGet("ResetAchievements/{userId}")]
        public IActionResult ResetAchievements(string userId)
        {
            var user = _context.Users
                .Include(u => u.Enrollments)
                .FirstOrDefault(u => u.Id == userId);

            if (user == null) return NotFound("User not found.");

            if (user.Enrollments != null && user.Enrollments.Any())
            {
                _context.Enrollments.RemoveRange(user.Enrollments);
                _context.SaveChanges();
            }

            return Redirect($"/UserProfile/{userId}");
        }

        [HttpGet("ResetAllProgress/{userId}")]
        public IActionResult ResetAllProgress(string userId)
        {
            var user = _context.Users
                .Include(u => u.Enrollments)
                .FirstOrDefault(u => u.Id == userId);

            if (user == null) return NotFound("User not found.");

            var progressToRemove = _context.ModuleProgress
                .Where(mp => mp.UserId == userId)
                .ToList();
            if (progressToRemove.Any()) _context.ModuleProgress.RemoveRange(progressToRemove);

            if (user.Enrollments != null && user.Enrollments.Any())
                _context.Enrollments.RemoveRange(user.Enrollments);

            var submissions = _context.CourseProjectSubmissions
                .Where(s => s.UserId == userId)
                .ToList();
            if (submissions.Any()) _context.CourseProjectSubmissions.RemoveRange(submissions);

            _context.SaveChanges();

            return Redirect($"/UserProfile/{userId}");
        }

        private string? ExtractCloudinaryPublicId(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            var uri = new Uri(url);
            var path = uri.AbsolutePath;

            // /dxixefedd/image/upload/v123456/skillbuilder/avatars/abc.jpg
            var uploadIndex = path.IndexOf("/upload/");
            if (uploadIndex == -1) return null;

            var publicIdWithVersion = path.Substring(uploadIndex + 8);

            // remove version segment (v123456/)
            var segments = publicIdWithVersion.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2) return null;

            var publicIdSegments = segments.Skip(1);
            var publicId = string.Join("/", publicIdSegments);

            return Path.ChangeExtension(publicId, null); // remove .jpg
        }

        [HttpGet("EditProfile")]
        public async Task<IActionResult> EditProfile()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return NotFound();

            var model = new UserProfileViewModel { User = user };
            return View("~/Views/Actions/EditProfile.cshtml", model);
        }

        [HttpPost("EditProfile")]
        public async Task<IActionResult> EditProfile(
            string FirstName,
            string LastName,
            string Email,
            DateTime? BirthDate,
            IFormFile UserAvatar,
            string CurrentPassword,
            string NewPassword,
            string ConfirmPassword)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return NotFound();

            user.FirstName = FirstName;
            user.LastName = LastName;

            if (BirthDate.HasValue)
            {
                user.BirthDate = DateOnly.FromDateTime(BirthDate.Value);
            }

            if (!string.Equals(user.Email, Email, StringComparison.OrdinalIgnoreCase))
            {
                bool emailExists = await _context.Users
                    .AnyAsync(u => u.Email == Email && u.Id != user.Id);
                if (emailExists)
                {
                    TempData["ErrorMessage"] = "This email is already in use.";
                    return RedirectToAction("EditProfile");
                }

                user.Email = Email;
                user.IsVerified = false;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                await _emailService.SendVerificationEmail(user.Email, user.Id, Url);

                await HttpContext.SignOutAsync("TahiAuth");
                TempData["SuccessMessage"] = "Your email has been updated. Please check your inbox to verify your new email address and log in again.";
                return RedirectToAction("Index", "Home");
            }

            if (UserAvatar != null && UserAvatar.Length > 0)
            {
                var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
                if (!allowedTypes.Contains(UserAvatar.ContentType))
                {
                    TempData["ErrorMessage"] = "Only JPG, PNG, or WEBP images are allowed.";
                    return RedirectToAction("EditProfile");
                }

                // Upload new avatar FIRST
                var newAvatarUrl = await _cloudinaryService.UploadImageAsync(
                    UserAvatar,
                    "skillbuilder/avatars"
                );

                if (!string.IsNullOrEmpty(newAvatarUrl))
                {
                    // Delete old avatar ONLY if it exists & is Cloudinary
                    if (!string.IsNullOrEmpty(user.UserAvatar) &&
                        user.UserAvatar.Contains("res.cloudinary.com"))
                    {
                        var oldPublicId = ExtractCloudinaryPublicId(user.UserAvatar);
                        if (!string.IsNullOrEmpty(oldPublicId))
                        {
                            await _cloudinaryService.DeleteImageAsync(oldPublicId);
                        }
                    }

                    user.UserAvatar = newAvatarUrl;
                }
            }

            if (!string.IsNullOrEmpty(CurrentPassword) ||
                !string.IsNullOrEmpty(NewPassword) ||
                !string.IsNullOrEmpty(ConfirmPassword))
            {
                if (string.IsNullOrEmpty(CurrentPassword) || string.IsNullOrEmpty(NewPassword) || string.IsNullOrEmpty(ConfirmPassword))
                {
                    TempData["ErrorMessage"] = "Please fill all password fields to update your password.";
                    return RedirectToAction("EditProfile");
                }

                if (!BCrypt.Net.BCrypt.Verify(CurrentPassword, user.PasswordHash))
                {
                    TempData["ErrorMessage"] = "Current password is incorrect.";
                    return RedirectToAction("EditProfile");
                }

                if (NewPassword != ConfirmPassword)
                {
                    TempData["ErrorMessage"] = "New password and confirmation do not match.";
                    return RedirectToAction("EditProfile");
                }

                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);
                TempData["SuccessMessage"] = "Password updated successfully!";
            }

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            await AddNotificationAsync(
                userId,
                $"✅ You have successfully edited your Profile."
            );

            if (string.IsNullOrEmpty((string)TempData["SuccessMessage"]))
                TempData["SuccessMessage"] = "Profile updated successfully!";

            return RedirectToAction("EditProfile");
        }

        [AllowAnonymous]
        [HttpGet("CheckEmailExist")]
        public JsonResult CheckEmailExist(string email)
        {
            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            bool exists = false;

            if (!string.IsNullOrEmpty(currentUserId))
                exists = _context.Users.Any(u => u.Email == email && u.Id != currentUserId);
            else
                exists = _context.Users.Any(u => u.Email == email);

            return Json(new { exists });
        }

        [HttpGet("VerifyOldPassword")]
        public IActionResult VerifyOldPassword(string oldPassword)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return NotFound();

            bool isValid = BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash);
            return Json(new { isValid });
        }

        [HttpPost("ResubmitFinalProject/{projectId}")]
        public async Task<IActionResult> ResubmitFinalProject(
            int projectId,
            [FromForm] string Title,
            [FromForm] string Description,
            [FromForm] IFormFile projectFile)
        {
            if (projectFile == null || projectFile.Length == 0)
                return BadRequest(new { success = false, message = "No file uploaded." });

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var project = await _context.CourseProjectSubmissions
                .Include(p => p.Course)
                    .ThenInclude(c => c.Artisan)
                .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId);

            if (project == null)
                return NotFound(new { success = false, message = "Project not found." });

            // Save uploaded file
            var allowedTypes = new[]
            {
                "image/jpeg", "image/png", "image/webp",
                "application/pdf"
            };

            if (!allowedTypes.Contains(projectFile.ContentType))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Invalid file type. Only images or PDF files are allowed."
                });
            }

            // Upload NEW project file first
            var folderPath = $"skillbuilder/projects/{project.CourseId}/{userId}";

            var newProjectUrl = await _cloudinaryService.UploadImageAsync(
                projectFile,
                folderPath
            );

            if (string.IsNullOrEmpty(newProjectUrl))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Failed to upload project file."
                });
            }

            // Delete OLD project file if it exists & is Cloudinary
            if (!string.IsNullOrEmpty(project.MediaUrl) &&
                project.MediaUrl.Contains("res.cloudinary.com"))
            {
                var oldPublicId = ExtractCloudinaryPublicId(project.MediaUrl);
                if (!string.IsNullOrEmpty(oldPublicId))
                {
                    await _cloudinaryService.DeleteImageAsync(oldPublicId);
                }
            }

            // Update project submission
            project.MediaUrl = newProjectUrl;
            project.SubmittedAt = DateTime.Now;
            project.Status = "Pending";
            project.Title = Title;
            project.Description = Description;

            _context.CourseProjectSubmissions.Update(project);

            // Notifications
            var artisanUserId = project.Course.Artisan?.UserId;
            if (!string.IsNullOrEmpty(artisanUserId))
            {
                await AddNotificationAsync(
                    artisanUserId,
                    $"📌 Project resubmitted by {User.Identity.Name} for course '{project.Course.Title}'."
                );
            }

            await AddNotificationAsync(
                userId,
                $"✅ You have successfully resubmitted your project for course '{project.Course.Title}'."
            );

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Project resubmitted successfully.", fileUrl = project.MediaUrl });
        }

        private async Task AddNotificationAsync(string userId, string message, string? actionText = null, string? actionUrl = null)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(message))
                return;

            var notification = new Notification
            {
                UserId = userId,
                Message = message,
                ActionText = actionText,
                ActionUrl = actionUrl,
                CreatedAt = DateTime.UtcNow,
                IsRead = false
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();
        }

        [HttpPost("ArchiveAccount")]
        public async Task<IActionResult> ArchiveAccount()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _context.Users
                .Include(u => u.Artisan)
                .FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return NotFound();

            user.IsArchived = true;

            if (user.Artisan != null)
            {
                user.Artisan.IsArchived = true;

                var artisanCourses = await _context.Courses
                    .Where(c => c.CreatedBy == user.Artisan.ArtisanId)
                    .ToListAsync();

                foreach (var course in artisanCourses)
                    course.IsArchived = true;
            }

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            await HttpContext.SignOutAsync("TahiAuth");
            return RedirectToAction("Index", "Home");
        }

        public class RescheduleSessionRequest
        {
            public DateTime NewDate { get; set; }
            public TimeSpan NewTime { get; set; }
            public string Message { get; set; } = "";
        }

        [HttpPost("RescheduleSessionByCourse/{courseId}")]
        public async Task<IActionResult> RescheduleSessionByCourse(int courseId, [FromBody] RescheduleSessionRequest data)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "Unauthorized" });

            // Get the latest session for this user & course
            var session = await _context.SupportSessionRequests
                .Where(r => r.CourseId == courseId && r.UserId == userId)
                .OrderByDescending(r => r.CreatedAt)
                .Include(r => r.Course)
                    .ThenInclude(c => c.Artisan)
                .FirstOrDefaultAsync();

            if (session == null)
                return NotFound(new { success = false, message = "Session not found" });

            session.SessionDate = data.NewDate;
            session.SessionTime = data.NewTime;
            session.Message = data.Message;
            session.Status = "Pending";
            session.ConfirmedAt = null;
            session.CompletedAt = null;

            _context.SupportSessionRequests.Update(session);
            await _context.SaveChangesAsync();

            // Notify artisan (simple notification, no action button)
            if (session.Course?.Artisan != null)
            {
                await AddNotificationAsync(
                    session.Course.Artisan.UserId,
                    $"📌 {session.User?.FirstName} has rescheduled a support session for '{session.Course.Title}'."
                );
            }

            // Notify learner (simple notification)
            await AddNotificationAsync(
                session.UserId,
                $"✅ You successfully rescheduled your support session for '{session.Course?.Title}'."
            );

            return Ok(new { success = true, message = "Session successfully rescheduled" });
        }

        [HttpPost("CancelSession/{requestId}")]
        public async Task<IActionResult> CancelSession(int requestId)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var request = await _context.SupportSessionRequests
                .Include(r => r.Course)
                .FirstOrDefaultAsync(r => r.Id == requestId && r.UserId == userId);

            if (request == null)
                return NotFound(new { success = false, message = "Session request not found." });

            request.Status = "Cancelled";
            await _context.SaveChangesAsync();

            // Notify learner with reschedule action
            await AddNotificationAsync(
                userId,
                $"You cancelled your session request for '{request.Course?.Title}'.",
                "Reschedule Session",
                $"/Support/RequestSession/{request.Course?.Id}"
            );

            // ✅ Notify the artisan (course creator)
            var artisanUserId = request.Course?.Artisan?.UserId;
            if (!string.IsNullOrEmpty(artisanUserId))
            {
                await AddNotificationAsync(
                    artisanUserId,
                    $"⚠️ {request.User?.FirstName} {request.User?.LastName} has cancelled their support session for '{request.Course?.Title}'."
                );
            }

            return Ok(new { success = true, message = "Session cancelled successfully!" });
        }
    }
}