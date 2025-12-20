using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillBuilder.Data;
using SkillBuilder.Models;
using SkillBuilder.Models.ViewModels;
using SkillBuilder.Services;

namespace SkillBuilder.Controllers
{
    [Authorize(Roles = "Artisan")]
    [Route("Artisan")]
    public class ArtisanController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly INotificationService _notificationService;
        private readonly ICloudinaryService _cloudinaryService;

        public ArtisanController(AppDbContext context, IWebHostEnvironment env, INotificationService notificationService, ICloudinaryService cloudinaryService)
        {
            _context = context;
            _env = env;
            _notificationService = notificationService;
            _cloudinaryService = cloudinaryService;
        }

        [AllowAnonymous]
        [HttpGet("")]
        [HttpGet("List")]
        public async Task<IActionResult> ArtisanList(string? search)
        {
            var query = _context.Artisans
                .Include(a => a.User)
                .Where(a => a.User != null && a.User.Role == "Artisan");

            if (!string.IsNullOrEmpty(search))
            {
                search = search.ToLower();
                query = query.Where(a =>
                    (a.User.FirstName + " " + a.User.LastName).ToLower().Contains(search));
            }

            var artisans = await query.ToListAsync();
            return View(artisans);
        }

        // GET: /Artisan/Resubmit/2
        [HttpGet("Resubmit/{applicationId}")]
        public async Task<IActionResult> Resubmit(int applicationId)
        {
            var app = await _context.ArtisanApplications
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.Id == applicationId && a.Status == "Rejected");

            if (app == null)
                return NotFound();

            return View("ResubmitApplication", app);
        }

        // POST: /Artisan/Resubmit/2
        [HttpPost("Resubmit/{applicationId}")]
        public async Task<IActionResult> Resubmit(int applicationId, IFormFile file)
        {
            var app = await _context.ArtisanApplications
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.Id == applicationId);

            if (app == null)
                return Json(new { success = false, message = "Application not found." });

            if (file != null && file.Length > 0)
            {
                var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                if (!Directory.Exists(uploadsDir))
                    Directory.CreateDirectory(uploadsDir);

                var filePath = Path.Combine(uploadsDir, file.FileName);
                using var stream = new FileStream(filePath, FileMode.Create);
                await file.CopyToAsync(stream);

                app.ApplicationFile = "/uploads/" + file.FileName;
                app.Status = "Pending"; // reset to pending
                await _context.SaveChangesAsync();

                // --- Get last rejection reason ---
                var lastRejection = await _context.Notifications
                    .Where(n => n.UserId == app.UserId &&
                                n.ActionUrl == $"/Artisan/Resubmit/{app.Id}" &&
                                n.ActionText == "Reject")
                    .OrderByDescending(n => n.CreatedAt)
                    .FirstOrDefaultAsync();

                string rejectionReason = lastRejection?.Message ?? "Please submit another Application.";

                // --- Notification to user ---
                _context.Notifications.Add(new Models.Notification
                {
                    UserId = app.UserId,
                    Message = "Your application has been resubmitted successfully.",
                    ActionUrl = null,
                    ActionText = null,
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false
                });

                await _context.SaveChangesAsync();
            }

            return Json(new { success = true, message = "Application resubmitted successfully." });
        }

        // Helper method
        private string? ExtractCloudinaryPublicId(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            var uri = new Uri(url);
            var path = uri.AbsolutePath;

            // /dxixefedd/image/upload/v123456/skillbuilder/works/abc.jpg
            var uploadIndex = path.IndexOf("/upload/");
            if (uploadIndex == -1) return null;

            var publicIdWithVersion = path.Substring(uploadIndex + 8);
            var segments = publicIdWithVersion.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2) return null;

            var publicIdSegments = segments.Skip(1);
            var publicId = string.Join("/", publicIdSegments);

            return Path.ChangeExtension(publicId, null); // remove extension
        }

        [HttpPost("AddWork")]
        public async Task<IActionResult> AddWork(string Title, string Caption, IFormFile ImageFile)
        {
            if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Caption) || ImageFile == null)
                return Json(new { success = false, message = "Invalid input" });

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var artisan = await _context.Artisans
                .Include(a => a.Works)
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.UserId == userId);

            if (artisan == null)
                return Json(new { success = false, message = "Unauthorized" });

            // --- Cloudinary upload ---
            string imageUrl = await _cloudinaryService.UploadImageAsync(ImageFile, "skillbuilder/works");
            if (string.IsNullOrEmpty(imageUrl))
            {
                return Json(new { success = false, message = "Failed to upload image." });
            }

            var newWork = new ArtisanWork
            {
                ArtisanId = artisan.ArtisanId,
                Title = Title,
                Caption = Caption,
                ImageUrl = imageUrl,
                PublishDate = DateTime.UtcNow
            };

            _context.ArtisanWorks.Add(newWork);
            await _context.SaveChangesAsync();

            // Notification
            await _notificationService.AddNotificationAsync(
                artisan.UserId,
                $"🎉 You successfully added a new work: '{newWork.Title}'."
            );

            // Return JSON for AJAX
            return Json(new
            {
                success = true,
                message = "Work added successfully",
                work = new
                {
                    id = newWork.Id,
                    title = newWork.Title,
                    caption = newWork.Caption,
                    imageUrl = newWork.ImageUrl,
                    publishDate = newWork.PublishDate.ToString("yyyy-MM-dd HH:mm")
                }
            });
        }

        [HttpGet("GetWork/{workId}")]
        public async Task<IActionResult> GetWork(int workId)
        {
            var work = await _context.ArtisanWorks
                .Include(w => w.Artisan)
                .FirstOrDefaultAsync(w => w.Id == workId);

            if (work == null)
                return NotFound();

            return Json(new
            {
                work.Id,
                work.Title,
                work.Caption,
                work.ImageUrl
            });
        }

        // POST: /Artisan/EditWork/5
        [HttpPost("EditWork/{workId}")]
        public async Task<IActionResult> EditWork(int workId, string Title, string Caption, IFormFile? ImageFile)
        {
            var work = await _context.ArtisanWorks
                .Include(w => w.Artisan)
                .FirstOrDefaultAsync(w => w.Id == workId);

            if (work == null)
                return Json(new { success = false, message = "Work not found" });

            bool hasChanges = false;

            if (!string.IsNullOrWhiteSpace(Title) && Title != work.Title)
            {
                work.Title = Title;
                hasChanges = true;
            }

            if (!string.IsNullOrWhiteSpace(Caption) && Caption != work.Caption)
            {
                work.Caption = Caption;
                hasChanges = true;
            }

            if (ImageFile != null && ImageFile.Length > 0)
            {
                // Delete old Cloudinary image if exists
                if (!string.IsNullOrEmpty(work.ImageUrl) && work.ImageUrl.Contains("res.cloudinary.com"))
                {
                    var oldPublicId = ExtractCloudinaryPublicId(work.ImageUrl);
                    if (!string.IsNullOrEmpty(oldPublicId))
                    {
                        await _cloudinaryService.DeleteImageAsync(oldPublicId);
                    }
                }

                // Upload new image to Cloudinary
                var newImageUrl = await _cloudinaryService.UploadImageAsync(ImageFile, "skillbuilder/works");
                if (!string.IsNullOrEmpty(newImageUrl))
                {
                    work.ImageUrl = newImageUrl;
                    hasChanges = true;
                }
            }

            if (hasChanges)
            {
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Json(new { success = false, message = "Work was modified/deleted by someone else." });
                }

                await _notificationService.AddNotificationAsync(
                    work.Artisan.UserId,
                    $"✏️ You successfully updated your work: '{work.Title}'."
                );
            }

            return Json(new { success = true, message = "Work updated successfully" });
        }

        // POST: /Artisan/DeleteWork/5
        [HttpPost("DeleteWork/{workId}")]
        public async Task<IActionResult> DeleteWork(int workId)
        {
            var work = await _context.ArtisanWorks
                .Include(w => w.Artisan) // Load the related Artisan
                .FirstOrDefaultAsync(w => w.Id == workId);

            if (work == null)
                return Json(new { success = false, message = "Work not found" });

            // --- Delete image from Cloudinary if exists ---
            if (!string.IsNullOrEmpty(work.ImageUrl) && work.ImageUrl.Contains("res.cloudinary.com"))
            {
                var publicId = ExtractCloudinaryPublicId(work.ImageUrl);
                if (!string.IsNullOrEmpty(publicId))
                {
                    await _cloudinaryService.DeleteImageAsync(publicId);
                }
            }

            _context.ArtisanWorks.Remove(work);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Json(new { success = false, message = "Work was already deleted or modified." });
            }

            if (work.ArtisanId != null)
            {
                await _notificationService.AddNotificationAsync(
                    work.Artisan.UserId,
                    $"🗑️ You successfully deleted your work: '{work.Title}'."
                );
            }

            return Json(new { success = true, message = "Work deleted successfully" });
        }
    }
}