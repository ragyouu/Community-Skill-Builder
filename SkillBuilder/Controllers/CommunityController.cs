using Appwrite.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillBuilder.Data;
using SkillBuilder.Models;
using SkillBuilder.Models.Entities;
using SkillBuilder.Models.ViewModels;
using SkillBuilder.Services;

namespace SkillBuilder.Controllers
{
    [Route("Community")]
    public class CommunityController : Controller
    {
        private readonly AppDbContext _context;
        private readonly INotificationService _notificationService;
        public CommunityController(AppDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        [HttpGet("Hub")]
        public async Task<IActionResult> CommunityHub(int? selectedCommunityId = null, string search = null)
        {
            ViewData["UseCourseNavbar"] = true;

            var communitiesQuery = _context.Communities
                .Where(c => c.IsPublished) // ✅ Only published communities
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
                communitiesQuery = communitiesQuery.Where(c => c.Name.ToLower().Contains(search.ToLower()));

            var communities = await communitiesQuery
                .OrderByDescending(c => c.MembersCount)
                .Take(20)
                .Select(c => new CommunitiesViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    Description = c.Description,
                    AvatarUrl = !string.IsNullOrEmpty(c.AvatarUrl) ? c.AvatarUrl : "/uploads/community-profile/default-community.png",
                    CoverImageUrl = !string.IsNullOrEmpty(c.CoverImageUrl) ? c.CoverImageUrl : "/uploads/community-banner/default-banner.png",
                    MembersCount = c.MembersCount,
                    CreatorId = c.CreatorId,
                    Category = c.Category
                })
                .ToListAsync();

            Community selectedCommunity = null;
            List<CommunityPostViewModel> posts = new List<CommunityPostViewModel>();

            if (selectedCommunityId.HasValue)
            {
                selectedCommunity = await _context.Communities
                    .Include(c => c.Memberships)
                    .Include(c => c.Creator)
                    .FirstOrDefaultAsync(c => c.Id == selectedCommunityId.Value && c.IsPublished); // Only published

                if (selectedCommunity != null)
                {
                    posts = await _context.CommunityPosts
                        .Include(p => p.Author)
                        .Where(p => p.CommunityId == selectedCommunity.Id && p.IsPublished)
                        .OrderByDescending(p => p.SubmittedAt)
                        .Select(p => new CommunityPostViewModel
                        {
                            Id = p.Id,
                            Title = p.Title,
                            Content = p.Content,
                            SubmittedAt = p.SubmittedAt,
                            ImageUrl = p.ImageUrl,
                            AuthorId = p.AuthorId,
                            AuthorName = p.Author.FirstName,
                            AuthorAvatarUrl = p.Author.UserAvatar,
                            CommunityName = p.Community.Name,
                            CommentsCount = 0,
                            Category = p.Category
                        })
                        .ToListAsync();
                }
                else
                {
                    // Redirect if the community is unpublished or doesn't exist
                    return RedirectToAction("CommunityHub");
                }
            }
            else
            {
                posts = await _context.CommunityPosts
                    .Include(p => p.Author)
                    .Where(p => p.IsPublished && p.CommunityId == null)
                    .OrderByDescending(p => p.SubmittedAt)
                    .Select(p => new CommunityPostViewModel
                    {
                        Id = p.Id,
                        Title = p.Title,
                        Content = p.Content,
                        SubmittedAt = p.SubmittedAt,
                        ImageUrl = p.ImageUrl,
                        AuthorId = p.AuthorId,
                        AuthorName = p.Author.FirstName,
                        AuthorAvatarUrl = p.Author.UserAvatar,
                        CommunityName = "Public",
                        CommentsCount = 0,
                        Category = p.Category
                    })
                    .ToListAsync();
            }

            var vm = new CommunityHubViewModel
            {
                Communities = communities,
                Posts = posts,
                SelectedCommunity = selectedCommunity
            };

            return View("CommunityHub", vm);
        }

        [HttpGet("GetCommunityDetailsPartial")]
        public async Task<IActionResult> GetCommunityDetailsPartial(int id)
        {
            var community = await _context.Communities
                .Include(c => c.Memberships)
                .Include(c => c.Creator)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (community == null)
                return NotFound();

            var currentUserId = User.FindFirst("UserId")?.Value;

            var detailsVm = new CommunityDetailsViewModel
            {
                SelectedCommunity = community,
                IsOwner = community.CreatorId == currentUserId,
                IsJoined = community.Memberships.Any(m => m.UserId == currentUserId)
            };

            return PartialView("~/Views/Shared/Sections/_CommunityDetailsSection.cshtml", detailsVm);
        }

        [HttpGet("GetPendingRequestsPartial")]
        public async Task<IActionResult> GetPendingRequestsPartial(int communityId)
        {
            // Get current user
            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Ensure the current user is part of this community
            var isMember = await _context.CommunityMemberships
                .AnyAsync(m => m.CommunityId == communityId && m.UserId == userId);

            if (!isMember)
                return Forbid();

            // Get pending join requests for this community only
            var pendingRequests = await _context.CommunityJoinRequests
                .Where(r => r.CommunityId == communityId && r.Status == "Pending")
                .Include(r => r.User)
                .ToListAsync();

            var model = new UserProfileViewModel
            {
                PendingJoinRequests = pendingRequests,
                SelectedCommunityId = communityId
            };

            return PartialView("Sections/UserNotebooks/MyCommunityNotebooks/_MyCommunityNotebookPending", model);
        }

        [HttpGet("GetMembersPartial")]
        public async Task<IActionResult> GetMembersPartial(int communityId)
        {
            // Get current user
            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Get all communities of the current user
            var userCommunities = await _context.Communities
                .Where(c => c.Memberships.Any(m => m.UserId == userId))
                .ToListAsync();

            // Get members of those communities
            var communityMembers = await _context.CommunityMemberships
                .Where(m => userCommunities.Select(c => c.Id).Contains(m.CommunityId))
                .Include(m => m.User)
                .ToListAsync();

            // Build dictionary: CommunityId => List of members
            var communityMembersDict = userCommunities.ToDictionary(
                c => c.Id,
                c => communityMembers
                        .Where(m => m.CommunityId == c.Id)
                        .Select(m => new CommunityMemberViewModel
                        {
                            UserId = m.UserId,
                            FullName = $"{m.User.FirstName} {m.User.LastName}", // combine names
                            Email = m.User.Email,
                            AvatarUrl = !string.IsNullOrEmpty(m.User.UserAvatar)
                                        ? m.User.UserAvatar
                                        : "/assets/Avatar/Sample10.svg",
                            Role = m.Role,
                            JoinedAt = m.JoinedAt,
                            CommunityId = m.CommunityId
                        })
                        .ToList()
            );

            var model = new UserProfileViewModel
            {
                MyCommunities = userCommunities.Select(c => new CommunitiesViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    Description = c.Description,
                    AvatarUrl = !string.IsNullOrEmpty(c.AvatarUrl) ? c.AvatarUrl : "/uploads/community-profile/default-community.png",
                    CoverImageUrl = !string.IsNullOrEmpty(c.CoverImageUrl) ? c.CoverImageUrl : "/uploads/community-banner/default-banner.png",
                    MembersCount = c.MembersCount,
                    CreatorId = c.CreatorId,
                    Category = c.Category
                }).ToList(),
                CommunityMembers = communityMembersDict,
                SelectedCommunityId = communityId
            };

            return PartialView("Sections/UserNotebooks/MyCommunityNotebooks/_MyCommunityNotebookAllMembers", model);
        }

        [HttpPost("CreatePost")]
        public async Task<IActionResult> CreatePost([FromForm] CreateCommunityPostViewModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "Invalid post data." });

            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound(new { success = false, message = "User not found." });

            string? imagePath = null;
            if (model.Image != null)
                imagePath = await SaveImage(model.Image, "community-posts");

            var post = new CommunityPost
            {
                CommunityId = null,
                Title = model.Title,
                Content = model.Content,
                AuthorId = userId,
                Category = model.Category,
                ImageUrl = imagePath,
                SubmittedAt = DateTime.UtcNow,
                IsPublished = true
            };

            _context.CommunityPosts.Add(post);
            await _context.SaveChangesAsync();

            await _notificationService.AddNotificationAsync(
                userId,
                $"✅ Your post '{post.Title}' has been successfully created in the public feed."
            );

            return Ok(new
            {
                success = true,
                message = "Post created successfully!",
                post = new
                {
                    id = post.Id,
                    title = post.Title,
                    content = post.Content,
                    imageUrl = post.ImageUrl,
                    submittedAt = post.SubmittedAt,
                    authorName = user.FirstName,
                    authorAvatarUrl = user.UserAvatar,
                    communityName = "Public" // since no community
                }
            });
        }

        [HttpPost("CreatePostInsideCommunity")]
        public async Task<IActionResult> CreatePostInsideCommunity([FromForm] CreateCommunityPostViewModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "Invalid post data." });

            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound(new { success = false, message = "User not found." });

            if (model.CommunityId <= 0)
                return BadRequest(new { success = false, message = "Community not selected." });

            var community = await _context.Communities.FindAsync(model.CommunityId);
            if (community == null)
                return NotFound(new { success = false, message = "Community not found." });

            string? imagePath = null;
            if (model.Image != null)
                imagePath = await SaveImage(model.Image, "community-posts");

            var post = new CommunityPost
            {
                CommunityId = model.CommunityId,
                Title = model.Title,
                Content = model.Content,
                AuthorId = userId,
                Category = string.IsNullOrEmpty(model.Category) ? "General" : model.Category,
                ImageUrl = imagePath,
                SubmittedAt = DateTime.UtcNow,
                IsPublished = true
            };

            _context.CommunityPosts.Add(post);
            await _context.SaveChangesAsync();

            var memberIds = await _context.CommunityMemberships
                .Where(m => m.CommunityId == post.CommunityId && m.UserId != userId)
                .Select(m => m.UserId)
                .ToListAsync();

            foreach (var memberId in memberIds)
            {
                await _notificationService.AddNotificationAsync(
                    memberId,
                    $"📝 New post '{post.Title}' has been added in the community '{community.Name}'."
                );
            }

            await _notificationService.AddNotificationAsync(
                userId,
                $"✅ Your post '{post.Title}' has been successfully created in '{community.Name}'."
            );

            return Ok(new
            {
                success = true,
                message = "Post created successfully!",
                post = new
                {
                    id = post.Id,
                    title = post.Title,
                    content = post.Content,
                    imageUrl = post.ImageUrl,
                    submittedAt = post.SubmittedAt,
                    authorName = user.FirstName,
                    communityName = community.Name
                }
            });
        }

        public class EditCommunityPostViewModel
        {
            public int PostId { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
            public IFormFile? Image { get; set; }
            public bool RemoveImage { get; set; }
        }

        [HttpPost("EditPost")]
        public async Task<IActionResult> EditPost([FromForm] EditCommunityPostViewModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "Invalid post data." });

            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var post = await _context.CommunityPosts
                .Include(p => p.Author)
                .Include(p => p.Community)
                    .ThenInclude(c => c.Creator)
                .FirstOrDefaultAsync(p => p.Id == model.PostId);

            if (post == null)
                return NotFound(new { success = false, message = "Post not found." });

            if (post.AuthorId != userId)
                return Forbid();

            post.Title = model.Title;
            post.Content = model.Content;

            // ✅ Handle image update/removal logic
            if (model.RemoveImage)
            {
                post.ImageUrl = null; // remove image from DB
            }
            else if (model.Image != null)
            {
                post.ImageUrl = await SaveImage(model.Image, "community-posts");
            }

            _context.CommunityPosts.Update(post);
            await _context.SaveChangesAsync();

            // ✅ Notify the user who edited
            await _notificationService.AddNotificationAsync(
                userId,
                $"✏️ Your post '{post.Title}' has been successfully updated."
            );

            return Ok(new
            {
                success = true,
                message = "Post updated successfully!",
                post = new
                {
                    id = post.Id,
                    title = post.Title,
                    content = post.Content,
                    imageUrl = post.ImageUrl,
                    submittedAt = post.SubmittedAt,
                    authorName = post.Author.FirstName,
                    communityName = post.Community?.Name ?? "Public"
                }
            });
        }

        public class ReportCommunityPostViewModel
        {
            public int PostId { get; set; }
            public string Reason { get; set; } = string.Empty;
            public string? Details { get; set; }
        }

        [HttpPost("ReportPost")]
        public async Task<IActionResult> ReportPost([FromForm] ReportCommunityPostViewModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "Invalid report data." });

            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var post = await _context.CommunityPosts
                .Include(p => p.Author)
                .Include(p => p.Community)
                    .ThenInclude(c => c.Creator)
                .FirstOrDefaultAsync(p => p.Id == model.PostId);

            if (post == null)
                return NotFound(new { success = false, message = "Post not found." });

            // Save report
            var report = new CommunityPostReport
            {
                PostId = post.Id,
                ReporterId = userId,
                Reason = model.Reason,
                Details = model.Details,
                ReportedAt = DateTime.UtcNow
            };
            _context.CommunityPostReports.Add(report);
            await _context.SaveChangesAsync();

            // 🔔 Notify the reporting user
            await _notificationService.AddNotificationAsync(
                userId,
                $"⚠️ You successfully reported the post '{post.Title}' for '{model.Reason}'."
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
                    $"⚠️ Post '{post.Title}' has been reported by {post.Author.FirstName} {post.Author.LastName}. Reason: {model.Reason}"
                );
            }

            return Ok(new { success = true, message = "Post reported successfully." });
        }

        [HttpPost("DeletePost")]
        public async Task<IActionResult> DeletePost(int postId)
        {
            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var post = await _context.CommunityPosts
                .Include(p => p.Author)
                .Include(p => p.Community)
                    .ThenInclude(c => c.Creator)
                .FirstOrDefaultAsync(p => p.Id == postId);

            if (post == null)
                return NotFound(new { success = false, message = "Post not found." });

            var communityOwnerId = post.Community?.CreatorId;

            // Only author or community owner can delete
            if (post.AuthorId != userId && communityOwnerId != userId)
                return Forbid();

            _context.CommunityPosts.Remove(post);
            await _context.SaveChangesAsync();

            // 🔔 Notify the user who deleted the post
            await _notificationService.AddNotificationAsync(
                userId,
                $"🗑️ You have successfully deleted the post '{post.Title}'."
            );

            // 🔔 Notify the community owner if different from the deleter
            if (!string.IsNullOrEmpty(communityOwnerId) && communityOwnerId != userId)
            {
                await _notificationService.AddNotificationAsync(
                    communityOwnerId,
                    $"🗑️ The post '{post.Title}' in your community '{post.Community.Name}' was deleted by {post.Author.FirstName} {post.Author.LastName}."
                );
            }

            return Ok(new { success = true, message = "Post deleted successfully." });
        }

        [HttpPost("Create")]
        public async Task<IActionResult> CreateCommunity(CreateCommunityViewModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest("Invalid data.");

            var userId = User.FindFirst("UserId")?.Value; // ✅ fixed
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound(new { success = false, message = "User not found." });

            if (!user.IsVerified)
                return BadRequest(new { success = false, message = "Please verify your email before creating a community." });

            if (user.IsDeactivated)
                return Forbid();

            if (user.Role != "Artisan" && user.Threads < 100)
            {
                return Json(new
                {
                    success = false,
                    insufficientThreads = true,
                    message = "You do not have enough threads to create a community."
                });
            }

            string avatarPath = null;
            string bannerPath = null;

            if (model.Avatar != null)
                avatarPath = await SaveImage(model.Avatar, "community-profile");

            if (model.Banner != null)
                bannerPath = await SaveImage(model.Banner, "community-banner");

            var community = new Community
            {
                Name = model.Name,
                Description = model.Description,
                Category = model.CategoryFinal,
                AvatarUrl = avatarPath ?? "/assets/Images/default-community.png",
                CoverImageUrl = bannerPath ?? "/assets/Images/default-banner.png",
                MembersCount = 1,
                CreatorId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Communities.Add(community);
            await _context.SaveChangesAsync();

            _context.CommunityMemberships.Add(new CommunityMembership
            {
                CommunityId = community.Id,
                UserId = userId,
                JoinedAt = DateTime.UtcNow,
                Role = "Owner"
            });

            user.Threads -= 100;
            _context.Users.Update(user);

            await _context.SaveChangesAsync();

            await _notificationService.AddNotificationAsync(
                userId,
                $"✅ Your Community '{community.Name}' has been created successfully."
            );

            // ✅ Notify all Admins
            var adminIds = await _context.Users
                .Where(u => u.Role == "Admin")
                .Select(u => u.Id)
                .ToListAsync();

            foreach (var adminId in adminIds)
            {
                await _notificationService.AddNotificationAsync(
                    adminId,
                    $"🌐 A new community '{community.Name}' was created by {user.FirstName} {user.LastName}."
                );
            }

            return Json(new
            {
                success = true,
                communityId = community.Id,
                message = $"Community '{community.Name}' created successfully."
            });
        }

        [HttpPost("Join")]
        public async Task<IActionResult> JoinCommunity(int communityId, string joinMessage)
        {
            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Check if the user is already a member
            var existingMembership = await _context.CommunityMemberships
                .FirstOrDefaultAsync(m => m.CommunityId == communityId && m.UserId == userId);
            if (existingMembership != null)
                return BadRequest(new { success = false, message = "You are already a member of this community." });

            // Check if there's already a pending join request
            var existingRequest = await _context.CommunityJoinRequests
                .FirstOrDefaultAsync(r => r.CommunityId == communityId && r.UserId == userId && r.Status == "Pending");
            if (existingRequest != null)
                return BadRequest(new { success = false, message = "You have already requested to join this community." });

            // Create a new join request
            var joinRequest = new CommunityJoinRequest
            {
                CommunityId = communityId,
                UserId = userId,
                ShortMessage = joinMessage?.Trim(),
                RequestedAt = DateTime.UtcNow,
                Status = "Pending"
            };

            _context.CommunityJoinRequests.Add(joinRequest);
            await _context.SaveChangesAsync();

            // Notify the community owner
            var community = await _context.Communities.FindAsync(communityId);
            if (community != null && !string.IsNullOrEmpty(community.CreatorId) && community.CreatorId != userId)
            {
                await _notificationService.AddNotificationAsync(
                    community.CreatorId,
                    $"👤 {User.Identity.Name} has requested to join your community '{community.Name}'."
                );
            }

            // Notify the user
            await _notificationService.AddNotificationAsync(
                userId,
                $"✅ Your request to join '{community?.Name}' has been sent and is pending approval."
            );

            return Ok(new { success = true, message = "Join request sent successfully!" });
        }

        private async Task<string> SaveImage(IFormFile file, string folderName)
        {
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", folderName);
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return $"/uploads/{folderName}/{fileName}";
        }

        [HttpGet("HasJoinRequest")]
        public async Task<IActionResult> HasJoinRequest(int communityId)
        {
            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var exists = await _context.CommunityJoinRequests
                .AnyAsync(r => r.CommunityId == communityId && r.UserId == userId && r.Status == "Pending");

            return Ok(new { success = true, hasRequest = exists });
        }

        [HttpPost("HandleJoinRequest")]
        public async Task<IActionResult> HandleJoinRequest([FromBody] JoinRequestActionModel model)
        {
            if (model == null)
                return BadRequest(new { success = false, message = "Invalid request data." });

            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var joinRequest = await _context.CommunityJoinRequests
                .Include(r => r.User)
                .Include(r => r.Community)
                    .ThenInclude(c => c.Creator)
                .FirstOrDefaultAsync(r => r.Id == model.RequestId);

            if (joinRequest == null)
                return NotFound(new { success = false, message = "Join request not found." });

            // Only the community owner can approve/reject
            if (joinRequest.Community.CreatorId != userId)
                return Forbid();

            if (model.Approve)
            {
                // ✅ Approve request: add member to community
                _context.CommunityMemberships.Add(new CommunityMembership
                {
                    CommunityId = joinRequest.CommunityId,
                    UserId = joinRequest.UserId,
                    JoinedAt = DateTime.UtcNow,
                    Role = "Member"
                });

                // Update community member count
                joinRequest.Community.MembersCount += 1;

                // Notify both user and owner
                await _notificationService.AddNotificationAsync(
                    joinRequest.UserId,
                    $"🎉 Your join request for '{joinRequest.Community.Name}' has been approved!"
                );

                await _notificationService.AddNotificationAsync(
                    joinRequest.Community.CreatorId,
                    $"✅ You approved {joinRequest.User.FirstName} {joinRequest.User.LastName}'s join request for '{joinRequest.Community.Name}'."
                );
            }
            else
            {
                // ❌ Reject request: notify both user and owner
                string reasonText = string.IsNullOrWhiteSpace(model.Reason)
                    ? ""
                    : $"\n\n📝 Reason: {model.Reason}";

                // Notify rejected user
                await _notificationService.AddNotificationAsync(
                    joinRequest.UserId,
                    $"🚫 Your join request for '{joinRequest.Community.Name}' has been rejected.{reasonText}"
                );

                // Notify owner about their own action
                await _notificationService.AddNotificationAsync(
                    joinRequest.Community.CreatorId,
                    $"🚫 You rejected {joinRequest.User.FirstName} {joinRequest.User.LastName}'s join request for '{joinRequest.Community.Name}'.{reasonText}"
                );
            }

            // 🔹 Delete the join request from database
            _context.CommunityJoinRequests.Remove(joinRequest);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = model.Approve
                    ? $"✅ {joinRequest.User.FirstName} {joinRequest.User.LastName} has been added to the community."
                    : $"❌ {joinRequest.User.FirstName} {joinRequest.User.LastName}'s request has been rejected."
            });
        }

        public class JoinRequestActionModel
        {
            public int RequestId { get; set; }
            public bool Approve { get; set; }
            public string? Reason { get; set; } // ✅ Added reason
        }

        // ✅ GET: Community/Edit/{id}
        // Returns a single community’s info for editing
        [HttpGet("Edit/{id}")]
        public async Task<IActionResult> EditCommunity(int id)
        {
            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var community = await _context.Communities.FirstOrDefaultAsync(c => c.Id == id);
            if (community == null)
                return NotFound(new { success = false, message = "Community not found." });

            // Only creator can edit
            if (community.CreatorId != userId)
                return Forbid();

            var vm = new CommunitiesViewModel
            {
                Id = community.Id,
                Name = community.Name,
                Description = community.Description,
                AvatarUrl = community.AvatarUrl,
                CoverImageUrl = community.CoverImageUrl,
                CreatorId = community.CreatorId,
                MembersCount = community.MembersCount,
                Category = community.Category // ✅ preload category
            };

            return Ok(vm);
        }

        // ✅ POST: Community/Edit
        [HttpPost("Edit")]
        public async Task<IActionResult> EditCommunity([FromForm] EditCommunityViewModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "Invalid form data." });

            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var community = await _context.Communities.FirstOrDefaultAsync(c => c.Id == model.Id);
            if (community == null)
                return NotFound(new { success = false, message = "Community not found." });

            if (community.CreatorId != userId)
                return Forbid();

            // ✅ Update basic info
            community.Name = model.Name;
            community.Description = model.Description;

            // ✅ Handle image uploads if provided
            if (model.Avatar != null)
                community.AvatarUrl = await SaveImage(model.Avatar, "community-profile");

            if (model.Banner != null)
                community.CoverImageUrl = await SaveImage(model.Banner, "community-banner");

            // ✅ Update category if included in model
            if (!string.IsNullOrEmpty(model.Category))
                community.Category = model.Category;

            _context.Communities.Update(community);
            await _context.SaveChangesAsync();

            // 🔔 Notify the owner
            await _notificationService.AddNotificationAsync(
                userId,
                $"✏️ Your community '{community.Name}' has been successfully updated."
            );

            return Ok(new
            {
                success = true,
                message = "Community updated successfully!",
                community = new
                {
                    id = community.Id,
                    name = community.Name,
                    description = community.Description,
                    avatarUrl = community.AvatarUrl,
                    bannerUrl = community.CoverImageUrl,
                    category = community.Category
                }
            });
        }

        public class EditCommunityViewModel
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? Description { get; set; }
            public IFormFile? Avatar { get; set; }
            public IFormFile? Banner { get; set; }
            public string? Category { get; set; }
        }

        [HttpPost("Delete")]
        public async Task<IActionResult> DeleteCommunity(int communityId)
        {
            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var community = await _context.Communities
                .Include(c => c.Memberships)
                .Include(c => c.Posts)
                .Include(c => c.JoinRequests)
                .FirstOrDefaultAsync(c => c.Id == communityId);

            if (community == null)
                return NotFound(new { success = false, message = "Community not found." });

            // Only creator can delete
            if (community.CreatorId != userId)
                return Forbid();

            // Notify members about deletion
            var memberIds = community.Memberships
                .Where(m => m.UserId != userId)
                .Select(m => m.UserId)
                .ToList();

            foreach (var memberId in memberIds)
            {
                await _notificationService.AddNotificationAsync(
                    memberId,
                    $"🗑️ The community '{community.Name}' has been deleted by its owner."
                );
            }

            // Notify owner
            await _notificationService.AddNotificationAsync(
                userId,
                $"✅ You have successfully deleted the community '{community.Name}'."
            );

            // Remove related posts
            if (community.Posts != null && community.Posts.Any())
                _context.CommunityPosts.RemoveRange(community.Posts);

            // Remove memberships
            if (community.Memberships != null && community.Memberships.Any())
                _context.CommunityMemberships.RemoveRange(community.Memberships);

            // Remove join requests
            if (community.JoinRequests != null && community.JoinRequests.Any())
                _context.CommunityJoinRequests.RemoveRange(community.JoinRequests);

            // Finally, remove the community
            _context.Communities.Remove(community);

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Community deleted successfully." });
        }

        [HttpPost("RemoveMember")]
        public async Task<IActionResult> RemoveMember([FromBody] RemoveMemberModel model)
        {
            if (model == null || model.CommunityId <= 0 || string.IsNullOrWhiteSpace(model.UserId))
                return BadRequest(new { success = false, message = "Invalid request data." });

            var currentUserId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(currentUserId))
                return Unauthorized();

            // ✅ Fetch community and check ownership
            var community = await _context.Communities
                .Include(c => c.Memberships)
                .FirstOrDefaultAsync(c => c.Id == model.CommunityId);

            if (community == null)
                return NotFound(new { success = false, message = "Community not found." });

            if (community.CreatorId != currentUserId)
                return Forbid();

            // ✅ Find the membership to remove
            var membership = await _context.CommunityMemberships
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.CommunityId == model.CommunityId && m.UserId == model.UserId);

            if (membership == null)
                return NotFound(new { success = false, message = "Member not found in this community." });

            // ✅ Prevent owner from removing themselves
            if (membership.UserId == currentUserId)
                return BadRequest(new { success = false, message = "You cannot remove yourself from your own community." });

            // ✅ Remove membership and update count
            _context.CommunityMemberships.Remove(membership);
            community.MembersCount = Math.Max(0, community.MembersCount - 1);
            await _context.SaveChangesAsync();

            // ✅ Notify the removed member
            await _notificationService.AddNotificationAsync(
                membership.UserId,
                $"🚫 You have been removed from the community '{community.Name}'.\n\n📝 Reason: {model.Reason}"
            );

            // ✅ Notify the owner (confirmation)
            await _notificationService.AddNotificationAsync(
                currentUserId,
                $"🗑️ You removed {membership.User.FirstName} {membership.User.LastName} from '{community.Name}'.\n\n📝 Reason: {model.Reason}"
            );

            return Ok(new
            {
                success = true,
                message = $"{membership.User.FirstName} {membership.User.LastName} has been removed from the community."
            });
        }

        public class RemoveMemberModel
        {
            public int CommunityId { get; set; }
            public string UserId { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
        }

        [HttpPost("ReportCommunity")]
        public async Task<IActionResult> ReportCommunity([FromForm] ReportCommunityViewModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "Invalid report data." });

            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var community = await _context.Communities
                .Include(c => c.Memberships)
                .FirstOrDefaultAsync(c => c.Id == model.CommunityId);

            if (community == null)
                return NotFound(new { success = false, message = "Community not found." });

            var report = new CommunityReport
            {
                CommunityId = community.Id,
                ReporterId = userId,
                Reason = model.Reason,
                Details = model.Details,
                ReportedAt = DateTime.UtcNow
            };

            _context.CommunityReports.Add(report);
            await _context.SaveChangesAsync();

            var reasonText = string.IsNullOrWhiteSpace(model.Details)
                ? model.Reason
                : $"{model.Reason}: {model.Details}";

            // Notify reporter
            await _notificationService.AddNotificationAsync(
                userId,
                $"⚠️ You reported the community '{community.Name}' for '{reasonText}'."
            );

            // Notify all admins
            var adminIds = await _context.Users
                .Where(u => u.Role == "Admin")
                .Select(u => u.Id)
                .ToListAsync();

            foreach (var adminId in adminIds)
            {
                await _notificationService.AddNotificationAsync(
                    adminId,
                    $"⚠️ Community '{community.Name}' was reported by a user. Reason: {reasonText}"
                );
            }

            return Ok(new { success = true, message = "Community reported successfully." });
        }

        public class ReportCommunityViewModel
        {
            public int CommunityId { get; set; }
            public string Reason { get; set; } = string.Empty;
            public string? Details { get; set; }
        }

    }

}