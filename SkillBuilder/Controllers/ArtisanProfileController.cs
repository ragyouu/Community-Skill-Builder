using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillBuilder.Data;
using SkillBuilder.Models;
using SkillBuilder.Models.ViewModels;
using SkillBuilder.Services;
using System.Security.Claims;

namespace SkillBuilder.Controllers
{
    [Authorize(AuthenticationSchemes = "TahiAuth", Roles = "Artisan")]
    [Route("ArtisanProfile")]
    public class ArtisanProfileController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notificationService;
        private readonly ICloudinaryService _cloudinaryService;

        public ArtisanProfileController(AppDbContext context, IPasswordHasher<User> passwordHasher, IEmailService emailService, INotificationService notificationService, ICloudinaryService cloudinaryService)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _emailService = emailService;
            _notificationService = notificationService;
            _cloudinaryService = cloudinaryService;
        }

        // Artisan Dashboard (Self view)
        [HttpGet("{id}")]
        public IActionResult ArtisanProfile(string id, int? communityId)
        {
            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (currentUserId == null || id != currentUserId)
                return Forbid();

            var artisan = _context.Artisans
                .Include(a => a.User)
                .FirstOrDefault(a => a.UserId == currentUserId);

            if (artisan == null)
                return NotFound();

            // Artisan's courses
            var courses = _context.Courses
                .Where(c => c.CreatedBy == artisan.UserId)
                .ToList();

            // Artisan's works
            var works = _context.ArtisanWorks
                .Where(w => w.ArtisanId == artisan.ArtisanId)
                .ToList();

            // Support session requests for artisan
            var artisanSupportRequests = _context.SupportSessionRequests
                .Include(r => r.User)
                .Include(r => r.Course)
                .Where(r => r.Course != null && r.Course.CreatedBy == artisan.UserId)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            // Project submissions for artisan's courses
            var projectSubmissions = _context.CourseProjectSubmissions
                .Include(p => p.User)
                .Include(p => p.Course)
                .Where(p => courses.Select(c => c.Id).Contains(p.CourseId))
                .ToList();

            // --- Communities ---
            var artisanCommunities = _context.Communities
                .Where(c => c.CreatorId == artisan.UserId && !c.IsArchived && c.IsPublished)
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

            // Communities artisan joined (but does NOT own)
            var joinedCommunities = _context.CommunityMemberships
                .Include(m => m.Community)
                .Where(m => m.UserId == artisan.UserId && m.Community.CreatorId != artisan.UserId)
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
            foreach (var community in artisanCommunities)
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

            var communityIds = artisanCommunities.Select(c => c.Id).ToList();
            var pendingJoinRequests = _context.CommunityJoinRequests
                .Where(r => communityIds.Contains(r.CommunityId) && r.Status == "Pending")
                .Include(r => r.User)
                .ToList();

            // Build view model
            var viewModel = new ArtisanProfileViewModel
            {
                Artisan = artisan,
                Courses = courses,
                ArtisanWorks = works,
                ArtisanSupportRequests = artisanSupportRequests,
                ProjectSubmissions = projectSubmissions,
                MyCommunities = artisanCommunities,
                JoinedCommunities = joinedCommunities,
                PendingJoinRequests = pendingJoinRequests,
                CommunityMembers = communityMembers,
                SelectedCommunityId = communityId ?? artisanCommunities.FirstOrDefault()?.Id
            };

            return View("~/Views/Profile/ArtisanProfile.cshtml", viewModel);
        }

        // Public view as Mentor
        [AllowAnonymous]
        [HttpGet("/ArtisanViewAsMentor/{id}")]
        public IActionResult ViewAsMentor(string id)
        {
            if (string.IsNullOrEmpty(id))
                return BadRequest();

            var artisan = _context.Artisans
                .Include(a => a.User)
                .FirstOrDefault(a => a.ArtisanId == id);

            if (artisan == null)
                return NotFound();

            var courses = _context.Courses
                .Where(c => c.CreatedBy == artisan.UserId)
                .ToList();

            var works = _context.ArtisanWorks
                .Where(w => w.ArtisanId == artisan.ArtisanId)
                .ToList();

            var viewModel = new ArtisanProfileViewModel
            {
                Artisan = artisan,
                Courses = courses,
                ArtisanWorks = works
            };

            return View("~/Views/Profile/ArtisanViewAsMentor.cshtml", viewModel);
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

        // GET: Edit profile (Artisan-specific)
        [HttpGet("EditProfileArtisan")]
        public async Task<IActionResult> EditProfileArtisan()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var artisan = await _context.Artisans
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.UserId == userId);

            if (artisan == null)
                return NotFound();

            var model = new ArtisanProfileViewModel
            {
                Artisan = artisan
            };

            return View("~/Views/Actions/ArtisanActions/EditProfileArtisan.cshtml", model);
        }

        // POST: Edit profile (Artisan-specific)
        [HttpPost("EditProfileArtisan")]
        public async Task<IActionResult> EditProfileArtisan(
            string FirstName,
            string LastName,
            string Email,
            string Hometown,
            string Introduction,
            IFormFile UserAvatar,
            string CurrentPassword,
            string NewPassword,
            string ConfirmPassword,
            [Bind(Prefix = "Artisan.Profession")] string Profession
        )

        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var artisan = await _context.Artisans
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.UserId == userId);

            if (artisan == null)
                return NotFound();

            bool hasChanges = false;

            // --- Sync fields ---
            if (artisan.FirstName != FirstName)
            {
                artisan.FirstName = FirstName;
                if (artisan.User != null) artisan.User.FirstName = FirstName;
                hasChanges = true;
            }

            if (artisan.LastName != LastName)
            {
                artisan.LastName = LastName;
                if (artisan.User != null) artisan.User.LastName = LastName;
                hasChanges = true;
            }

            // Handle "Others" profession input
            var finalProfession = Profession?.Trim();

            if (finalProfession == "Others")
            {
                var professionOther = Request.Form["ProfessionOther"].ToString()?.Trim();
                if (!string.IsNullOrEmpty(professionOther))
                {
                    finalProfession = professionOther;
                }
                else
                {
                    TempData["ErrorMessage"] = "Please enter your profession when 'Others' is selected.";
                    return RedirectToAction("EditProfileArtisan");
                }
            }

            if (artisan.Profession != finalProfession)
            {
                artisan.Profession = finalProfession;
                hasChanges = true;
            }

            // Continue with Hometown, Introduction, Email, etc.
            if (artisan.Hometown != Hometown) { artisan.Hometown = Hometown; hasChanges = true; }
            if (artisan.Introduction != Introduction) { artisan.Introduction = Introduction; hasChanges = true; }

            // --- Email update ---
            if (artisan.User != null && artisan.User.Email != Email)
            {
                bool emailExists = await _context.Users.AnyAsync(u => u.Email == Email && u.Id != artisan.User.Id);
                if (emailExists)
                {
                    TempData["ErrorMessage"] = "This email is already in use.";
                    return RedirectToAction("EditProfileArtisan");
                }

                artisan.User.Email = Email;
                artisan.User.IsVerified = false;
                var verificationToken = Guid.NewGuid().ToString();
                artisan.User.EmailVerificationToken = verificationToken;

                _context.Entry(artisan.User).State = EntityState.Modified;
                await _context.SaveChangesAsync();

                // Send verification email
                var verificationLink = Url.Action("VerifyEmail", "ArtisanProfile", new { token = verificationToken }, Request.Scheme);
                await _emailService.SendVerificationEmailWithLink(Email, verificationLink);

                // Sign out
                await HttpContext.SignOutAsync("TahiAuth");

                // Add notification BEFORE log out
                if (_notificationService != null)
                {
                    await _notificationService.AddNotificationAsync(artisan.User.Id, "Your profile and email was updated. Please verify it to continue using your account.");
                }

                TempData["SuccessMessage"] = "Email changed. Please verify your new email to log in.";
                return RedirectToAction("Index", "Home");
            }

            // --- Avatar ---
            if (UserAvatar != null && UserAvatar.Length > 0 && artisan.User != null)
            {
                var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
                if (!allowedTypes.Contains(UserAvatar.ContentType))
                {
                    TempData["ErrorMessage"] = "Only JPG, PNG, or WEBP images are allowed.";
                    return RedirectToAction("EditProfileArtisan");
                }

                // Upload new avatar to Cloudinary
                var newAvatarUrl = await _cloudinaryService.UploadImageAsync(
                    UserAvatar,
                    "skillbuilder/avatars"
                );

                if (!string.IsNullOrEmpty(newAvatarUrl))
                {
                    // Delete old avatar if it exists in Cloudinary
                    if (!string.IsNullOrEmpty(artisan.User.UserAvatar) &&
                        artisan.User.UserAvatar.Contains("res.cloudinary.com"))
                    {
                        var oldPublicId = ExtractCloudinaryPublicId(artisan.User.UserAvatar);
                        if (!string.IsNullOrEmpty(oldPublicId))
                        {
                            await _cloudinaryService.DeleteImageAsync(oldPublicId);
                        }
                    }

                    artisan.User.UserAvatar = newAvatarUrl;
                    artisan.UserAvatar = newAvatarUrl;
                    hasChanges = true;
                }
            }

            // --- Password ---
            if (!string.IsNullOrEmpty(NewPassword) && NewPassword == ConfirmPassword && !string.IsNullOrEmpty(CurrentPassword))
            {
                if (!string.IsNullOrEmpty(artisan.User.PasswordHash))
                {
                    bool passwordMatches = BCrypt.Net.BCrypt.Verify(CurrentPassword, artisan.User.PasswordHash);
                    if (passwordMatches)
                    {
                        artisan.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);
                        _context.Entry(artisan.User).State = EntityState.Modified;
                        hasChanges = true;

                        // Add notification for password change
                        if (_notificationService != null)
                        {
                            await _notificationService.AddNotificationAsync(artisan.User.Id, "Your password has been updated successfully.");
                        }
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "Current password is incorrect.";
                        return RedirectToAction("EditProfileArtisan");
                    }
                }
            }

            // --- Save changes ---
            if (hasChanges)
            {
                await _context.SaveChangesAsync();

                // Add general notification
                if (_notificationService != null)
                {
                    await _notificationService.AddNotificationAsync(artisan.User.Id, "Your profile has been updated successfully.");
                }

                TempData["SuccessMessage"] = "Profile updated successfully.";
            }
            else
            {
                TempData["SuccessMessage"] = "No changes were made.";
            }

            return RedirectToAction("EditProfileArtisan");
        }

        [AllowAnonymous]
        [HttpGet("CheckEmailExist")]
        public JsonResult CheckEmailExist(string email)
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value; // may be null
            bool exists = false;

            if (!string.IsNullOrEmpty(currentUserId))
            {
                exists = _context.Users.Any(u => u.Email == email && u.Id != currentUserId);
            }
            else
            {
                exists = _context.Users.Any(u => u.Email == email);
            }

            return Json(new { exists });
        }

        [AllowAnonymous]
        [HttpGet("/ArtisanProfile/VerifyEmail")]
        public async Task<IActionResult> VerifyArtisanEmail(string token)
        {
            if (string.IsNullOrEmpty(token))
                return BadRequest("Invalid verification token.");

            var artisan = await _context.Users.FirstOrDefaultAsync(u => u.EmailVerificationToken == token && u.Artisan != null);
            if (artisan == null)
                return NotFound("Invalid or expired token.");

            artisan.IsVerified = true;
            artisan.EmailVerificationToken = null;

            _context.Users.Update(artisan);
            await _context.SaveChangesAsync();

            return Content("✅ Your email has been verified successfully!");
        }

        [HttpGet("VerifyOldPassword")]
        public IActionResult VerifyOldPassword(string oldPassword)
        {
            var artisanId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(artisanId)) return Unauthorized();

            var artisan = _context.Artisans.Include(a => a.User).FirstOrDefault(a => a.UserId == artisanId);
            if (artisan?.User == null) return NotFound();

            bool isValid = BCrypt.Net.BCrypt.Verify(oldPassword, artisan.User.PasswordHash);
            return Json(new { isValid });
        }

        [HttpPost("ArchiveArtisanAccount")]
        public async Task<IActionResult> ArchiveArtisanAccount()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // Load artisan + related User
            var artisan = await _context.Artisans
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.UserId == userId);

            if (artisan == null || artisan.User == null)
                return NotFound();

            // Soft delete the artisan profile
            artisan.IsArchived = true;

            // Soft delete the related User
            artisan.User.IsArchived = true;

            // Soft delete all courses created by this artisan
            var artisanCourses = await _context.Courses
                .Where(c => c.CreatedBy == artisan.UserId)
                .ToListAsync();

            foreach (var course in artisanCourses)
            {
                course.IsArchived = true;
            }

            _context.Artisans.Update(artisan);
            _context.Users.Update(artisan.User);
            _context.Courses.UpdateRange(artisanCourses);

            await _context.SaveChangesAsync();

            // Sign out after archiving
            await HttpContext.SignOutAsync("TahiAuth");

            return RedirectToAction("Index", "Home");
        }
    }
}