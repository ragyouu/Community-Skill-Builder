using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using SkillBuilder.Data;
using SkillBuilder.Models;
using SkillBuilder.Models.ViewModels;
using SkillBuilder.Services;
using System.Text.RegularExpressions;

namespace SkillBuilder.Controllers
{
    [Route("ArtisanActions")]
    [Authorize(Roles = "Artisan")]
    public class ArtisanCourseController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly INotificationService _notificationService;
        private readonly ICloudinaryService _cloudinaryService;

        public ArtisanCourseController(AppDbContext context, IWebHostEnvironment env, INotificationService notificationService, ICloudinaryService cloudinaryService)
        {
            _context = context;
            _env = env;
            _notificationService = notificationService;
            _cloudinaryService = cloudinaryService;
        }

        private string? ExtractCloudinaryPublicId(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath;

                var uploadIndex = path.IndexOf("/upload/");
                if (uploadIndex == -1) return null;

                var publicIdWithVersion = path[(uploadIndex + 8)..];

                // Remove version if exists (v1234567890)
                var parts = publicIdWithVersion.Split('/');
                if (parts.Length > 1 && parts[0].StartsWith("v"))
                {
                    parts = parts[1..]; // skip version
                }

                var publicId = string.Join("/", parts);
                return Path.ChangeExtension(publicId, null);
            }
            catch
            {
                return null;
            }
        }

        [HttpGet("CreateCourse")]
        public IActionResult CreateCourse()
        {
            var viewModel = new CourseBuilderViewModel
            {
                Course = new Course(),
                LearningObjectives = new List<string> { "" },
                Modules = new List<CourseModuleViewModel>(),
                Materials = new List<CourseMaterialViewModel>()
            };

            return View("~/Views/Actions/ArtisanActions/CreateCourse.cshtml", viewModel);
        }

        public static string StripHtml(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            return Regex.Replace(input, "<.*?>", string.Empty);
        }

        [HttpPost("CreateCourse")]
        [RequestSizeLimit(50 * 1024 * 1024)] // Allow up to 50 MB for this action
        public async Task<IActionResult> CreateCourse(CourseBuilderViewModel model)
        {
            if (!ModelState.IsValid)
            {
                foreach (var state in ModelState)
                {
                    if (state.Value.Errors.Any())
                    {
                        Console.WriteLine($"{state.Key} : {string.Join(", ", state.Value.Errors.Select(e => e.ErrorMessage))}");
                    }
                }
            }

            var userId = User.FindFirst("UserId")?.Value;
            var artisan = await _context.Artisans.FirstOrDefaultAsync(a => a.UserId == userId);
            if (artisan == null) return Unauthorized();

            var course = model.Course!;
            course.CreatedBy = artisan.ArtisanId;
            course.CreatedAt = DateTime.UtcNow;
            course.Duration = $"{model.DurationValue} {model.DurationUnit}";

            // ---------------------- STRIP HTML FROM TINYMCE FIELDS ----------------------
            course.Overview = System.Net.WebUtility.HtmlDecode(StripHtml(course.Overview));
            course.Requirements = System.Net.WebUtility.HtmlDecode(StripHtml(course.Requirements));
            course.FullDescription = System.Net.WebUtility.HtmlDecode(StripHtml(course.FullDescription));

            if (course.Category == "Other" && !string.IsNullOrWhiteSpace(model.CustomCategory))
            {
                course.Category = model.CustomCategory.Trim();
            }

            // ------------------ FREE COURSE LOGIC ------------------
            course.IsFree = model.Course.IsFree;

            // Only set DesiredThreads if course is not free
            course.DesiredThreads = !model.Course.IsFree ? model.Course.DesiredThreads : 0M;

            // ✅ File Uploads with Validation
            if (model.ImageFile != null)
            {
                if (model.ImageFile.Length > 5 * 1024 * 1024) // 5 MB
                {
                    ModelState.AddModelError("ImageFile", "Image file must be under 5 MB.");
                    return View("~/Views/Actions/ArtisanActions/CreateCourse.cshtml", model);
                }
                course.ImageUrl = await _cloudinaryService.UploadImageAsync(
                    model.ImageFile,
                    "skillbuilder/courses/images"
                );
            }

            if (model.VideoFile != null)
            {
                if (model.VideoFile.Length > 50 * 1024 * 1024) // 50 MB
                {
                    ModelState.AddModelError("VideoFile", "Video file must be under 50 MB.");
                    return View("~/Views/Actions/ArtisanActions/CreateCourse.cshtml", model);
                }

                // 🔥 Delete old Cloudinary video if updating (optional, if editing)
                if (!string.IsNullOrEmpty(course.Video))
                {
                    var oldId = ExtractCloudinaryPublicId(course.Video);
                    if (oldId != null)
                        await _cloudinaryService.DeleteVideoAsync(oldId);
                }

                // ☁️ Upload new video
                course.Video = await _cloudinaryService.UploadVideoAsync(
                    model.VideoFile,
                    "skillbuilder/courses/videos"
                );
            }

            if (model.ThumbnailFile != null)
            {
                if (model.ThumbnailFile.Length > 5 * 1024 * 1024) // 5 MB
                {
                    ModelState.AddModelError("ThumbnailFile", "Thumbnail must be under 5 MB.");
                    return View("~/Views/Actions/ArtisanActions/CreateCourse.cshtml", model);
                }
                course.Thumbnail = await _cloudinaryService.UploadImageAsync(
                    model.ThumbnailFile,
                    "skillbuilder/courses/thumbnails"
                );
            }

            // Generate course link if missing
            course.Link = string.IsNullOrWhiteSpace(course.Link)
                ? System.Text.RegularExpressions.Regex.Replace(
                    course.Title.ToLower(), @"[^a-z0-9]+", "-"
                  ).Trim('-') + "-" + Guid.NewGuid().ToString("N")[..8]
                : course.Link;

            // Learning objectives
            course.WhatToLearn = model.LearningObjectives != null
                ? string.Join("||", model.LearningObjectives.Where(o => !string.IsNullOrWhiteSpace(o)))
                : null;

            if (model.FinalProject != null)
            {
                course.FinalProjectTitle = model.FinalProject.Title;
                course.FinalProjectDescription = model.FinalProject.Description;
            }

            _context.Courses.Add(course);
            await _context.SaveChangesAsync();

            // ✅ Modules
            if (model.Modules != null)
            {
                for (int i = 0; i < model.Modules.Count; i++)
                {
                    var moduleVm = model.Modules[i];
                    var courseModule = new CourseModule
                    {
                        CourseId = course.Id,
                        Title = moduleVm.Title,
                        Order = i,
                        Contents = new List<ModuleContent>()
                    };

                    _context.CourseModules.Add(courseModule);
                    await _context.SaveChangesAsync();

                    for (int j = 0; j < moduleVm.Lessons.Count; j++)
                    {
                        var lesson = moduleVm.Lessons[j];
                        var moduleContent = new ModuleContent
                        {
                            CourseModuleId = courseModule.Id,
                            Title = lesson.Title,
                            ContentType = lesson.LessonType ?? "Text",
                            Order = j,
                            Duration = $"{lesson.DurationValue} {lesson.DurationUnit}",
                            ContentText = lesson.ContentText
                        };

                        // -------------------- HANDLE LESSON MEDIA (CLOUDINARY) --------------------
                        if (lesson.LessonType == "Image + Text" && lesson.ImageFile != null)
                        {
                            moduleContent.MediaUrl = await _cloudinaryService.UploadImageAsync(
                                lesson.ImageFile,
                                "skillbuilder/lessons/images"
                            );
                        }
                        else if (lesson.LessonType == "Video + Text" && lesson.VideoFile != null)
                        {
                            moduleContent.MediaUrl = await _cloudinaryService.UploadVideoAsync(
                                lesson.VideoFile,
                                "skillbuilder/lessons/videos"
                            );
                        }

                        _context.ModuleContents.Add(moduleContent);
                        await _context.SaveChangesAsync();

                        // -------------------- SAVE INTERACTIVE CONTENTS --------------------
                        if (lesson.InteractiveContents != null && lesson.InteractiveContents.Any())
                        {
                            foreach (var ic in lesson.InteractiveContents)
                            {
                                // Map CorrectAnswer key to actual option text
                                var correctAnswerValue = ic.CorrectAnswer switch
                                {
                                    "OptionA" => ic.OptionA,
                                    "OptionB" => ic.OptionB,
                                    "OptionC" => ic.OptionC,
                                    "OptionD" => ic.OptionD,
                                    _ => ic.CorrectAnswer // fallback in case it's already the text
                                };

                                var interactiveContent = new InteractiveContent
                                {
                                    ModuleContentId = moduleContent.Id,
                                    ContentType = string.IsNullOrWhiteSpace(ic.ContentType) ? "Text" : ic.ContentType,
                                    ContentText = System.Net.WebUtility.HtmlDecode(ic.ContentText ?? ""),
                                    OptionA = ic.OptionA,
                                    OptionB = ic.OptionB,
                                    OptionC = ic.OptionC,
                                    OptionD = ic.OptionD,
                                    CorrectAnswer = correctAnswerValue,
                                    ReflectionMinChars = ic.ReflectionMinChars
                                };
                                _context.InteractiveContents.Add(interactiveContent);
                            }
                            await _context.SaveChangesAsync();
                        }

                        // -------------------- EXISTING QUIZ LOGIC --------------------
                        if (lesson.LessonType == "Quiz" && lesson.QuizQuestions.Any())
                        {
                            foreach (var q in lesson.QuizQuestions)
                            {
                                var quiz = new QuizQuestion
                                {
                                    ModuleContentId = moduleContent.Id,
                                    Question = q.QuestionText,
                                    OptionA = q.OptionA,
                                    OptionB = q.OptionB,
                                    OptionC = q.OptionC,
                                    OptionD = q.OptionD,
                                    CorrectAnswer = q.CorrectAnswer ?? ""
                                };
                                _context.QuizQuestions.Add(quiz);
                            }
                            await _context.SaveChangesAsync();
                        }
                    }
                }
            }

            // ✅ Materials
            if (model.Materials != null)
            {
                foreach (var mat in model.Materials)
                {
                    if (mat.UploadFile != null && mat.UploadFile.Length > 0)
                    {
                        var filePath = await SaveFileAsync(mat.UploadFile, "course-materials");

                        var courseMaterial = new CourseMaterial
                        {
                            CourseId = course.Id,
                            Title = mat.Title,
                            Description = mat.Description,
                            FilePath = filePath,
                            FileName = mat.UploadFile.FileName,
                            FileSize = mat.UploadFile.Length
                        };
                        _context.CourseMaterials.Add(courseMaterial);
                    }
                }
            }

            await _context.SaveChangesAsync();

            await _notificationService.AddNotificationAsync(
                artisan.UserId,
                $"✅ Your course '{course.Title}' has been successfully created!"
            );

            // ✅ Notify Admin(s)
            var adminUsers = await _context.Users
                .Where(u => u.Role == "Admin")
                .ToListAsync();

            foreach (var admin in adminUsers)
            {
                await _notificationService.AddNotificationAsync(
                    admin.Id,
                    $"📢 New course created: '{course.Title}' by {artisan.FirstName} {artisan.LastName}."
                );
            }

            return Redirect($"/ArtisanProfile/{artisan.ArtisanId}");
        }

        private async Task<string> SaveFileAsync(IFormFile file, string folderName)
        {
            if (file == null || file.Length == 0)
                throw new InvalidOperationException("File is empty or missing.");

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            // ✅ Updated file type and size validation
            if (extension is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp")
            {
                if (file.Length > 20 * 1024 * 1024) // 20 MB
                    throw new InvalidOperationException("Image files must be under 20 MB.");
            }
            else if (extension is ".pdf" or ".docx" or ".txt")
            {
                if (file.Length > 100 * 1024 * 1024) // 100 MB
                    throw new InvalidOperationException("Document files must be under 100 MB.");
            }
            else if (extension is ".mp4" or ".mov" or ".avi")
            {
                if (file.Length > 200 * 1024 * 1024) // 200 MB
                    throw new InvalidOperationException("Video files must be under 200 MB.");
            }
            else
            {
                throw new InvalidOperationException("Unsupported file type.");
            }

            // ✅ Save file to uploads folder
            var uploadsRoot = Path.Combine(_env.WebRootPath, "uploads", folderName);
            if (!Directory.Exists(uploadsRoot))
                Directory.CreateDirectory(uploadsRoot);

            var fileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadsRoot, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(stream);

            return $"/uploads/{folderName}/{fileName}";
        }

        [HttpGet("EditCourse/{courseId}")]
        public async Task<IActionResult> EditCourse(int courseId)
        {
            var userId = User.FindFirst("UserId")?.Value;
            var artisan = await _context.Artisans.FirstOrDefaultAsync(a => a.UserId == userId);
            if (artisan == null) return Unauthorized();

            var course = await _context.Courses
                .Include(c => c.Materials)
                .Include(c => c.CourseModules)
                    .ThenInclude(m => m.Contents)
                        .ThenInclude(mc => mc.QuizQuestions)
                .Include(c => c.CourseModules)
                    .ThenInclude(m => m.Contents)
                        .ThenInclude(mc => mc.InteractiveContents)
                .FirstOrDefaultAsync(c => c.Id == courseId && c.CreatedBy == artisan.ArtisanId);

            if (course == null) return NotFound();

            var viewModel = new CourseBuilderViewModel
            {
                Course = course,
                DurationValue = int.TryParse(course.Duration?.Split(' ')[0], out var val) ? val : 0,
                DurationUnit = course.Duration?.Split(' ').ElementAtOrDefault(1) ?? "hours",
                LearningObjectives = course.WhatToLearn?.Split("||").ToList() ?? new List<string> { "" },
                IsFree = course.IsFree,
                DesiredThreads = course.DesiredThreads,
                Modules = course.CourseModules
                    .OrderBy(m => m.Order)
                    .Select(m => new CourseModuleViewModel
                    {
                        Title = m.Title,
                        Order = m.Order,
                        Lessons = m.Contents
                            .OrderBy(c => c.Order)
                            .Select(l =>
                            {
                                int durationValue = 0;
                                string durationUnit = "minutes";
                                if (!string.IsNullOrWhiteSpace(l.Duration))
                                {
                                    var parts = l.Duration.Split(' ');
                                    int.TryParse(parts[0], out durationValue);
                                    durationUnit = parts.Length > 1 ? parts[1] : "minutes";
                                }

                                return new LessonViewModel
                                {
                                    Id = l.Id,
                                    Title = l.Title ?? "",
                                    LessonType = l.ContentType ?? "",
                                    DurationValue = durationValue,
                                    DurationUnit = durationUnit,
                                    ContentText = l.ContentText ?? "",
                                    ImageFile = null,
                                    VideoFile = null,
                                    ExistingImageUrl = l.ContentType == "Image + Text" ? l.MediaUrl : null,
                                    ExistingVideoUrl = l.ContentType == "Video + Text" ? l.MediaUrl : null,
                                    QuizQuestions = l.QuizQuestions.Select(q => new QuizQuestionViewModel
                                    {
                                        Id = q.Id,
                                        QuestionText = q.Question,
                                        OptionA = q.OptionA,
                                        OptionB = q.OptionB,
                                        OptionC = q.OptionC,
                                        OptionD = q.OptionD,
                                        CorrectAnswer = q.CorrectAnswer
                                    }).ToList(),
                                    InteractiveContents = l.InteractiveContents.Select(ic => new InteractiveContentViewModel
                                    {
                                        Id = ic.Id,
                                        ContentType = ic.ContentType,
                                        ContentText = ic.ContentText,
                                        OptionA = ic.OptionA,
                                        OptionB = ic.OptionB,
                                        OptionC = ic.OptionC,
                                        OptionD = ic.OptionD,
                                        CorrectAnswer = ic.CorrectAnswer,
                                        ReflectionMinChars = ic.ReflectionMinChars
                                    }).ToList()
                                };
                            }).ToList()
                    }).ToList(),
                Materials = course.Materials
                    .Select(mat => new CourseMaterialViewModel
                    {
                        Id = mat.Id,
                        Title = mat.Title,
                        FileName = mat.FileName,
                        FilePath = mat.FilePath,
                        FileSize = mat.FileSize,
                        Description = mat.Description
                    }).ToList(),
                FinalProject = new FinalProjectViewModel
                {
                    Title = course.FinalProjectTitle,
                    Description = course.FinalProjectDescription
                },
            };

            return View("~/Views/Actions/ArtisanActions/EditCourse.cshtml", viewModel);
        }

        [HttpPost("EditCourse")]
        public async Task<IActionResult> EditCourse(int courseId, CourseBuilderViewModel model)
        {
            if (!ModelState.IsValid)
                return View("~/Views/Actions/ArtisanActions/EditCourse.cshtml", model);

            var userId = User.FindFirst("UserId")?.Value;
            var artisan = await _context.Artisans.FirstOrDefaultAsync(a => a.UserId == userId);
            if (artisan == null) return Unauthorized();

            var course = await _context.Courses
                .Include(c => c.Materials)
                .Include(c => c.CourseModules)
                    .ThenInclude(m => m.Contents)
                        .ThenInclude(mc => mc.QuizQuestions)
                .Include(c => c.CourseModules)
                    .ThenInclude(m => m.Contents)
                        .ThenInclude(mc => mc.InteractiveContents)
                .FirstOrDefaultAsync(c => c.Id == courseId && c.CreatedBy == artisan.ArtisanId);

            if (course == null) return NotFound();

            // ------------------ Update Basic Course Info ------------------
            course.Title = model.Course.Title;
            course.Category = model.Course.Category;

            if (course.Category == "Other" && !string.IsNullOrWhiteSpace(model.CustomCategory))
            {
                course.Category = model.CustomCategory.Trim();
            }

            course.Overview = model.Course.Overview;
            course.Difficulty = model.Course.Difficulty;
            course.Duration = $"{model.DurationValue} {model.DurationUnit}";
            course.IsFree = model.Course.IsFree;
            course.DesiredThreads = model.Course.IsFree
                ? (decimal?)null
                : model.Course.DesiredThreads;
            course.Requirements = model.Course.Requirements;
            course.WhatToLearn = model.LearningObjectives != null
                ? string.Join("||", model.LearningObjectives.Where(o => !string.IsNullOrWhiteSpace(o)))
                : null;
            course.FullDescription = model.Course.FullDescription;

            course.Overview = System.Net.WebUtility.HtmlDecode(StripHtml(course.Overview));
            course.Requirements = System.Net.WebUtility.HtmlDecode(StripHtml(course.Requirements));
            course.FullDescription = System.Net.WebUtility.HtmlDecode(StripHtml(course.FullDescription));

            // Update media
            if (model.ImageFile != null)
            {
                if (!string.IsNullOrEmpty(course.ImageUrl))
                {
                    var oldId = ExtractCloudinaryPublicId(course.ImageUrl);
                    if (oldId != null)
                        await _cloudinaryService.DeleteImageAsync(oldId);
                }

                course.ImageUrl = await _cloudinaryService.UploadImageAsync(
                    model.ImageFile,
                    "skillbuilder/courses/images"
                );
            }
            if (model.VideoFile != null)
            {
                if (!string.IsNullOrEmpty(course.Video))
                {
                    var oldId = ExtractCloudinaryPublicId(course.Video);
                    if (oldId != null)
                        await _cloudinaryService.DeleteVideoAsync(oldId);  // use DeleteVideoAsync
                }

                course.Video = await _cloudinaryService.UploadVideoAsync(    // use UploadVideoAsync
                    model.VideoFile,
                    "skillbuilder/courses/videos"
                );
            }
            if (model.ThumbnailFile != null)
            {
                if (!string.IsNullOrEmpty(course.Thumbnail))
                {
                    var oldId = ExtractCloudinaryPublicId(course.Thumbnail);
                    if (oldId != null)
                        await _cloudinaryService.DeleteImageAsync(oldId);
                }

                course.Thumbnail = await _cloudinaryService.UploadImageAsync(
                    model.ThumbnailFile,
                    "skillbuilder/courses/thumbnails"
                );
            }

            // ------------------ Update Final Project ------------------
            if (model.FinalProject != null)
            {
                course.FinalProjectTitle = model.FinalProject.Title;
                course.FinalProjectDescription = model.FinalProject.Description;
            }

            _context.Courses.Update(course);

            // ------------------ Update Modules and Lessons ------------------
            if (model.Modules != null)
            {
                var formModuleIds = model.Modules.Select(m => m.Id).ToList();

                // Remove deleted modules
                var modulesToDelete = course.CourseModules
                    .Where(m => !formModuleIds.Contains(m.Id))
                    .ToList();
                if (modulesToDelete.Any()) _context.CourseModules.RemoveRange(modulesToDelete);

                for (int i = 0; i < model.Modules.Count; i++)
                {
                    var moduleVm = model.Modules[i];
                    CourseModule courseModule;

                    // Existing module
                    if (moduleVm.Id > 0)
                    {
                        courseModule = course.CourseModules.First(m => m.Id == moduleVm.Id);
                        courseModule.Title = moduleVm.Title;
                        courseModule.Order = i;
                        _context.CourseModules.Update(courseModule);
                    }
                    else
                    {
                        // New module
                        courseModule = new CourseModule
                        {
                            CourseId = course.Id,
                            Title = moduleVm.Title,
                            Order = i
                        };
                        _context.CourseModules.Add(courseModule);
                        await _context.SaveChangesAsync(); // Need ID for lessons
                    }

                    // ------------------ Lessons ------------------
                    if (moduleVm.Lessons != null)
                    {
                        var lessonIds = moduleVm.Lessons.Select(l => l.Id).ToList();
                        var lessonsToDelete = courseModule.Contents
                            .Where(l => !lessonIds.Contains(l.Id))
                            .ToList();

                        if (lessonsToDelete.Any())
                        {
                            foreach (var lesson in lessonsToDelete)
                            {
                                if (!string.IsNullOrEmpty(lesson.MediaUrl))
                                {
                                    var publicId = ExtractCloudinaryPublicId(lesson.MediaUrl);
                                    if (publicId != null)
                                    {
                                        if (lesson.ContentType == "Video + Text")
                                            await _cloudinaryService.DeleteVideoAsync(publicId);
                                        else
                                            await _cloudinaryService.DeleteImageAsync(publicId);
                                    }
                                }
                            }

                            _context.ModuleContents.RemoveRange(lessonsToDelete);
                        }

                        for (int j = 0; j < moduleVm.Lessons.Count; j++)
                        {
                            var lessonVm = moduleVm.Lessons[j];
                            ModuleContent? lesson = null;

                            // Determine if it's an existing lesson
                            if (lessonVm.Id > 0)
                            {
                                lesson = courseModule.Contents.FirstOrDefault(l => l.Id == lessonVm.Id);
                            }

                            if (lesson != null)
                            {
                                // Update existing lesson
                                lesson.Title = lessonVm.Title;
                                lesson.ContentType = lessonVm.LessonType ?? "Text";
                                lesson.Duration = $"{lessonVm.DurationValue} {lessonVm.DurationUnit}";
                                lesson.ContentText = lessonVm.ContentText;
                            }
                            else
                            {
                                // New lesson
                                lesson = new ModuleContent
                                {
                                    CourseModuleId = courseModule.Id,
                                    Title = lessonVm.Title,
                                    ContentType = lessonVm.LessonType ?? "Text",
                                    Duration = $"{lessonVm.DurationValue} {lessonVm.DurationUnit}",
                                    ContentText = lessonVm.ContentText
                                };
                                _context.ModuleContents.Add(lesson);
                                await _context.SaveChangesAsync(); // Save to get ID for quizzes/interactive
                            }

                            // ------------------ Handle media ------------------
                            if (lessonVm.LessonType == "Image + Text")
                            {
                                if (lessonVm.ImageFile != null || string.IsNullOrEmpty(lessonVm.ExistingImageUrl))
                                {
                                    if (!string.IsNullOrEmpty(lesson.MediaUrl))
                                    {
                                        var oldId = ExtractCloudinaryPublicId(lesson.MediaUrl);
                                        if (oldId != null)
                                            await _cloudinaryService.DeleteImageAsync(oldId);
                                    }

                                    if (lessonVm.ImageFile != null)
                                    {
                                        lesson.MediaUrl = await _cloudinaryService.UploadImageAsync(
                                            lessonVm.ImageFile,
                                            "skillbuilder/lessons/images"
                                        );
                                    }
                                    else
                                    {
                                        lesson.MediaUrl = null;
                                    }
                                }
                            }
                            else if (lessonVm.LessonType == "Video + Text")
                            {
                                if (lessonVm.VideoFile != null || string.IsNullOrEmpty(lessonVm.ExistingVideoUrl))
                                {
                                    if (!string.IsNullOrEmpty(lesson.MediaUrl))
                                    {
                                        var oldId = ExtractCloudinaryPublicId(lesson.MediaUrl);
                                        if (oldId != null)
                                            await _cloudinaryService.DeleteVideoAsync(oldId);
                                    }

                                    if (lessonVm.VideoFile != null)
                                    {
                                        lesson.MediaUrl = await _cloudinaryService.UploadVideoAsync(
                                            lessonVm.VideoFile,
                                            "skillbuilder/lessons/videos"
                                        );
                                    }
                                    else
                                    {
                                        lesson.MediaUrl = null;
                                    }
                                }
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(lesson.MediaUrl))
                                {
                                    var oldId = ExtractCloudinaryPublicId(lesson.MediaUrl);
                                    if (oldId != null)
                                    {
                                        if (lesson.ContentType?.StartsWith("Video") == true)
                                            await _cloudinaryService.DeleteVideoAsync(oldId);
                                        else
                                            await _cloudinaryService.DeleteImageAsync(oldId);
                                    }
                                }
                                lesson.MediaUrl = null;
                            }

                            // Update DB if it was an existing lesson
                            if (lessonVm.Id > 0 && lesson != null)
                            {
                                _context.ModuleContents.Update(lesson);
                            }

                            // Now it’s safe to initialize collections
                            lesson.QuizQuestions ??= new List<QuizQuestion>();
                            lesson.InteractiveContents ??= new List<InteractiveContent>();

                            // ------------------ Quiz Questions ------------------
                            if (lessonVm.LessonType == "Quiz")
                            {
                                lesson.QuizQuestions ??= new List<QuizQuestion>();

                                var quizIds = lessonVm.QuizQuestions?.Select(q => q.Id).ToList() ?? new List<int>();
                                var quizzesToDelete = lesson.QuizQuestions
                                    .Where(q => !quizIds.Contains(q.Id))
                                    .ToList();
                                if (quizzesToDelete.Any()) _context.QuizQuestions.RemoveRange(quizzesToDelete);

                                for (int qIndex = 0; qIndex < (lessonVm.QuizQuestions?.Count ?? 0); qIndex++)
                                {
                                    var qVm = lessonVm.QuizQuestions[qIndex];
                                    QuizQuestion quiz;

                                    if (qVm.Id > 0)
                                    {
                                        quiz = lesson.QuizQuestions.First(q => q.Id == qVm.Id);
                                        quiz.Question = qVm.QuestionText;
                                        quiz.OptionA = qVm.OptionA;
                                        quiz.OptionB = qVm.OptionB;
                                        quiz.OptionC = qVm.OptionC;
                                        quiz.OptionD = qVm.OptionD;
                                        quiz.CorrectAnswer = qVm.CorrectAnswer;
                                        _context.QuizQuestions.Update(quiz);
                                    }
                                    else
                                    {
                                        quiz = new QuizQuestion
                                        {
                                            ModuleContentId = lesson.Id,
                                            Question = qVm.QuestionText,
                                            OptionA = qVm.OptionA,
                                            OptionB = qVm.OptionB,
                                            OptionC = qVm.OptionC,
                                            OptionD = qVm.OptionD,
                                            CorrectAnswer = qVm.CorrectAnswer
                                        };
                                        _context.QuizQuestions.Add(quiz);
                                    }
                                }
                            }

                            var interactiveIds = lessonVm.InteractiveContents?.Select(ic => ic.Id).ToList() ?? new List<int>();
                            var interactivesToDelete = lesson.InteractiveContents
                                .Where(ic => !interactiveIds.Contains(ic.Id))
                                .ToList();
                            if (interactivesToDelete.Any()) _context.InteractiveContents.RemoveRange(interactivesToDelete);

                            if (lessonVm.InteractiveContents != null)
                            {
                                foreach (var icVm in lessonVm.InteractiveContents)
                                {
                                    InteractiveContent ic;
                                    if (icVm.Id > 0)
                                    {
                                        ic = lesson.InteractiveContents.First(c => c.Id == icVm.Id);

                                        bool mediaChanged = icVm.MediaFile != null || string.IsNullOrEmpty(icVm.ExistingMediaUrl);

                                        if (mediaChanged)
                                        {
                                            if (!string.IsNullOrEmpty(ic.MediaUrl))
                                            {
                                                var oldId = ExtractCloudinaryPublicId(ic.MediaUrl);
                                                if (oldId != null)
                                                {
                                                    if (ic.ContentType?.StartsWith("Video") == true)
                                                        await _cloudinaryService.DeleteVideoAsync(oldId);
                                                    else
                                                        await _cloudinaryService.DeleteImageAsync(oldId);
                                                }
                                            }

                                            if (icVm.MediaFile != null)
                                            {
                                                if (icVm.ContentType?.StartsWith("Video") == true)
                                                    ic.MediaUrl = await _cloudinaryService.UploadVideoAsync(icVm.MediaFile, "skillbuilder/interactive");
                                                else
                                                    ic.MediaUrl = await _cloudinaryService.UploadImageAsync(icVm.MediaFile, "skillbuilder/interactive");
                                            }
                                            else
                                            {
                                                ic.MediaUrl = null;
                                            }
                                        }
                                        else
                                        {
                                            ic.MediaUrl = icVm.ExistingMediaUrl;
                                        }

                                        ic.ContentType = icVm.ContentType;
                                        ic.ContentText = System.Net.WebUtility.HtmlDecode(icVm.ContentText ?? "");
                                        ic.OptionA = icVm.OptionA;
                                        ic.OptionB = icVm.OptionB;
                                        ic.OptionC = icVm.OptionC;
                                        ic.OptionD = icVm.OptionD;
                                        ic.CorrectAnswer = icVm.CorrectAnswer;
                                        ic.ReflectionMinChars = icVm.ReflectionMinChars;

                                        _context.InteractiveContents.Update(ic);
                                    }
                                    else
                                    {
                                        ic = new InteractiveContent
                                        {
                                            ModuleContentId = lesson.Id,
                                            ContentType = icVm.ContentType,
                                            ContentText = System.Net.WebUtility.HtmlDecode(icVm.ContentText ?? ""),
                                            OptionA = icVm.OptionA,
                                            OptionB = icVm.OptionB,
                                            OptionC = icVm.OptionC,
                                            OptionD = icVm.OptionD,
                                            CorrectAnswer = icVm.CorrectAnswer,
                                            ReflectionMinChars = icVm.ReflectionMinChars
                                        };

                                        if (icVm.MediaFile != null)
                                        {
                                            if (icVm.ContentType?.StartsWith("Video") == true)
                                                ic.MediaUrl = await _cloudinaryService.UploadVideoAsync(icVm.MediaFile, "skillbuilder/interactive");
                                            else
                                                ic.MediaUrl = await _cloudinaryService.UploadImageAsync(icVm.MediaFile, "skillbuilder/interactive");
                                        }

                                        _context.InteractiveContents.Add(ic);
                                        await _context.SaveChangesAsync();
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (model.MaterialsToDelete != null && model.MaterialsToDelete.Any())
            {
                var materialsToDelete = course.Materials
                    .Where(m => model.MaterialsToDelete.Contains(m.Id) && m.Id > 0)
                    .ToList();
                if (materialsToDelete.Any())
                {
                    // Delete actual files
                    foreach (var mat in materialsToDelete)
                    {
                        if (!string.IsNullOrEmpty(mat.FilePath))
                        {
                            var path = Path.Combine(_env.WebRootPath, mat.FilePath);
                            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                        }
                    }

                    _context.CourseMaterials.RemoveRange(materialsToDelete);
                }
            }

            if (model.Materials != null && model.Materials.Any())
            {
                foreach (var matVm in model.Materials)
                {
                    CourseMaterial material;

                    if (matVm.Id > 0)
                    {
                        material = course.Materials.First(m => m.Id == matVm.Id);
                        material.Title = matVm.Title;
                        material.Description = matVm.Description;

                        if (matVm.UploadFile != null)
                        {
                            // Delete old file if exists
                            if (!string.IsNullOrEmpty(material.FilePath))
                            {
                                var oldPath = Path.Combine(_env.WebRootPath, material.FilePath);
                                if (System.IO.File.Exists(oldPath))
                                    System.IO.File.Delete(oldPath);
                            }

                            var filePath = await SaveFileAsync(matVm.UploadFile, "course-materials");
                            material.FilePath = filePath;
                            material.FileName = matVm.UploadFile.FileName;
                            material.FileSize = matVm.UploadFile.Length;
                        }

                        _context.CourseMaterials.Update(material);
                    }
                    else if (matVm.UploadFile != null)
                    {
                        // New material
                        var filePath = await SaveFileAsync(matVm.UploadFile, "course-materials");
                        material = new CourseMaterial
                        {
                            CourseId = course.Id,
                            Title = matVm.Title,
                            Description = matVm.Description,
                            FileName = matVm.UploadFile.FileName,
                            FileSize = matVm.UploadFile.Length,
                            FilePath = filePath
                        };
                        _context.CourseMaterials.Add(material);
                    }
                }
            }

            await _context.SaveChangesAsync();

            await _notificationService.AddNotificationAsync(
                artisan.UserId,
                $"✏️ Your course '{course.Title}' has been successfully updated!"
            );

            return RedirectToAction("ArtisanProfile", "ArtisanProfile", new { id = artisan.ArtisanId });
        }

        [HttpPost("UploadInteractiveImage")]
        [Authorize(Roles = "Artisan")]
        public async Task<IActionResult> UploadInteractiveImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            var imageUrl = await _cloudinaryService.UploadImageAsync(
                file,
                "skillbuilder/interactive"
            );

            return Json(new { location = imageUrl });
        }
    }
}