using BCrypt.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using Org.BouncyCastle.Crypto.Generators;
using SkillBuilder.Data;
using SkillBuilder.Models;
using SkillBuilder.Services;
using System.Security.Claims;

namespace SkillBuilder.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notificationService;

        public AccountController(AppDbContext context, IConfiguration config, IWebHostEnvironment env, IEmailService emailService, INotificationService notificationService)
        {
            _context = context;
            _config = config;
            _env = env;
            _emailService = emailService;
            _notificationService = notificationService;
        }

        [HttpPost("/signup")]
        public async Task<IActionResult> Signup([FromBody] SignupRequest model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();

                return BadRequest(new { message = string.Join(" ", errors) });
            }

            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
            if (existingUser != null)
                return BadRequest(new { message = "Email already exists." });

            var newUser = new User
            {
                Id = await GenerateUserId("Learner"),
                FirstName = model.FirstName,
                LastName = model.LastName,
                BirthDate = model.BirthDate,
                Email = model.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
                Role = "Learner",
                CreatedAt = DateTime.UtcNow,
                IsVerified = false,
                UserAvatar = "/assets/Avatar/Sample10.svg",
                Threads = 0.00M
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            await _notificationService.AddNotificationAsync(newUser.Id, $"🎉 Welcome {newUser.FirstName}! Your account has been successfully created.");

            var claims = new List<Claim>
            {
                new Claim("UserId", newUser.Id),
                new Claim(ClaimTypes.NameIdentifier, newUser.Id),
                new Claim(ClaimTypes.Name, newUser.FirstName + " " + newUser.LastName),
                new Claim(ClaimTypes.Email, newUser.Email),
                new Claim(ClaimTypes.Role, newUser.Role)
            };

            var identity = new ClaimsIdentity(claims, "TahiAuth");
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync("TahiAuth", principal);

            return Ok(new { message = "Account created successfully. Please select your interests." });
        }

        [HttpPost("/send-verification")]
        public async Task<IActionResult> SendVerification([FromBody] string email)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null) return NotFound(new { message = "User not found." });

            // ✅ use email service correctly
            await _emailService.SendVerificationEmail(user.Email, user.Id, Url);

            return Ok(new { message = "Verification email sent." });
        }

        [HttpPost("/signup-artisan")]
        public async Task<IActionResult> SignupArtisan([FromBody] ArtisanSignupRequest model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();

                return BadRequest(new { message = string.Join(" ", errors) });
            }

            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
            if (existingUser != null)
                return BadRequest(new { message = "Email already exists." });

            var newUserId = await GenerateUserId("Artisan");

            var newUser = new User
            {
                Id = newUserId,
                FirstName = model.FirstName,
                LastName = model.LastName,
                Email = model.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
                Role = "Artisan",
                CreatedAt = DateTime.UtcNow,
                IsVerified = false,
                UserAvatar = "/assets/Avatar/Sample10.svg"
            };

            var artisan = new Artisan
            {
                ArtisanId = newUserId,
                UserId = newUserId,
                FirstName = model.FirstName,
                LastName = model.LastName,
                Role = "Artisan",
                UserAvatar = newUser.UserAvatar,
                IsApproved = false
            };

            _context.Users.Add(newUser);
            _context.Artisans.Add(artisan);
            await _context.SaveChangesAsync();

            await _notificationService.AddNotificationAsync(newUser.Id, $"Welcome {newUser.FirstName}! Your Artisan account has been successfully created.");

            var claims = new List<Claim>
            {
                new Claim("UserId", newUser.Id),
                new Claim(ClaimTypes.NameIdentifier, newUser.Id),
                new Claim(ClaimTypes.Name, newUser.FirstName + " " + newUser.LastName),
                new Claim(ClaimTypes.Email, newUser.Email),
                new Claim(ClaimTypes.Role, newUser.Role)
            };

            var identity = new ClaimsIdentity(claims, "TahiAuth");
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync("TahiAuth", principal);

            // ✅ send verification email after signup
            await _emailService.SendVerificationEmail(newUser.Email, newUser.Id, Url);

            return Ok(new { message = "Artisan account created. Please check your email to verify." });
        }

        private async Task<string> GenerateUserId(string rolePrefix)
        {
            var prefix = rolePrefix switch
            {
                "Learner" => "L",
                "Artisan" => "A",
                "Admin" => "ADMIN",
                _ => "L"
            };

            string newId;
            bool exists;

            do
            {
                var random = new Random();
                var digits = random.Next(0, 9999999).ToString("D7");
                newId = $"{prefix}{digits}";

                exists = await _context.Users.AnyAsync(u => u.Id == newId);

            } while (exists);

            return newId;
        }

        [HttpPost("/submit-artisan-application")]
        public async Task<IActionResult> SubmitArtisanApplication(IFormFile file, string profession, string hometown, string introduction)
        {
            var userId = User.FindFirst("UserId")?.Value;
            if (userId == null) return Unauthorized();

            string? filePath = null;
            if (file != null)
            {
                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads/artisan-application");
                Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                filePath = Path.Combine("uploads/artisan-application", uniqueFileName).Replace("\\", "/");

                using var stream = new FileStream(Path.Combine(_env.WebRootPath, filePath), FileMode.Create);
                await file.CopyToAsync(stream);
            }

            var application = new ArtisanApplication
            {
                UserId = userId,
                Profession = profession,
                Hometown = hometown,
                Introduction = introduction,
                SubmittedAt = DateTime.UtcNow,
                Status = "Pending",
                ApplicationFile = filePath
            };
            _context.ArtisanApplications.Add(application);

            var artisan = await _context.Artisans.FirstOrDefaultAsync(a => a.UserId == userId);
            if (artisan == null)
            {
                artisan = new Artisan
                {
                    UserId = userId,
                    Profession = profession,
                    Hometown = hometown,
                    Introduction = introduction,
                    ApplicationFile = filePath
                };
                _context.Artisans.Add(artisan);
            }
            else
            {
                artisan.Profession = profession;
                artisan.Hometown = hometown;
                artisan.Introduction = introduction;
                artisan.ApplicationFile = filePath;
            }

            await _context.SaveChangesAsync();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user != null && !user.IsVerified)
            {
                await _notificationService.AddNotificationAsync(
                    user.Id,
                    "✅ Your artisan application has been submitted successfully and is now pending review."
                );

                if (!user.IsVerified)
                {
                    await _emailService.SendVerificationEmail(user.Email, user.Id, Url);
                }
            }

            return Ok(new { message = "Application submitted successfully!" });
        }

        [AllowAnonymous]
        [HttpGet("/verify")]
        public async Task<IActionResult> VerifyEmail(string id)
        {
            if (string.IsNullOrEmpty(id))
                return BadRequest("Invalid verification link.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
                return NotFound("User not found.");

            if (!user.IsVerified)
            {
                user.IsVerified = true;
                await _context.SaveChangesAsync();

                await _notificationService.AddNotificationAsync(
                    user.Id,
                    "✅ Your email has been successfully verified. You can now access all features."
                );
            }

            return Content("✅ Your email has been verified successfully!");
        }

        [HttpGet("/force-logout")]
        public async Task<IActionResult> ForceLogout()
        {
            await HttpContext.SignOutAsync("TahiAuth");
            return Redirect("/");
        }

        [HttpPost("/login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Invalid input." });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);

            if (user == null)
                return Unauthorized(new { message = "Invalid email or password." });

            if (user.IsArchived)
                return Unauthorized(new { message = "Your account has been archived. Please contact support." });

            if (!BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
                return Unauthorized(new { message = "Invalid email or password." });

            var claims = new List<Claim>
            {
                new Claim("UserId", user.Id),
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.FirstName + " " + user.LastName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role)
            };

            if (user.Role == "Artisan")
            {
                var artisan = await _context.Artisans.FirstOrDefaultAsync(a => a.UserId == user.Id);
                if (artisan != null)
                {
                    claims.Add(new Claim("IsApproved", artisan.IsApproved.ToString()));
                }
            }

            string redirectUrl;

            switch (user.Role)
            {
                case "Artisan":
                    var artisan = await _context.Artisans.FirstOrDefaultAsync(a => a.UserId == user.Id);
                    if (artisan != null)
                    {
                        claims.Add(new Claim("ArtisanId", artisan.ArtisanId));
                        claims.Add(new Claim("IsApproved", artisan.IsApproved.ToString()));

                        // ✅ Correct Artisan redirect
                        redirectUrl = $"/ArtisanProfile/{artisan.ArtisanId}";
                    }
                    else
                    {
                        redirectUrl = "/ArtisanProfile"; // fallback
                    }
                    break;

                case "Admin":
                    var admin = await _context.Admins.FirstOrDefaultAsync(a => a.UserId == user.Id);
                    if (admin != null)
                    {
                        claims.Add(new Claim("AdminId", admin.AdminId));

                        // ✅ Correct Admin redirect
                        redirectUrl = $"/AdminProfile/{user.Id}";
                    }
                    else
                    {
                        redirectUrl = "/AdminProfile"; // fallback
                    }
                    break;

                default:
                    // ✅ Learner redirect
                    redirectUrl = $"/UserProfile/{user.Id}";
                    break;
            }

            var identity = new ClaimsIdentity(claims, "TahiAuth");
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync("TahiAuth", principal);

            return Ok(new
            {
                success = true,
                message = "Login successful.",
                role = user.Role,
                redirectUrl,
                userId = user.Id,
                isVerified = user.IsVerified
            });
        }

        [HttpPost("/logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("TahiAuth");
            return Redirect("/");
        }

        [HttpPost("/forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = "Invalid email." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
            if (user == null)
                return NotFound(new { message = "Email not found." });

            // Generate OTP
            var random = new Random();
            var otp = random.Next(100000, 999999).ToString();

            // Save OTP to user or database (with expiration)
            user.PasswordResetOtp = otp;
            user.OtpExpiry = DateTime.UtcNow.AddMinutes(10);
            await _context.SaveChangesAsync();

            // Send OTP via email
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Tahi Support", _config["Email:Sender"]));
            message.To.Add(MailboxAddress.Parse(user.Email));
            message.Subject = "Your Password Reset OTP";

            var builder = new BodyBuilder
            {
                HtmlBody = $"<p>Your OTP for password reset is: <strong>{otp}</strong></p>" +
                           $"<p>This code expires in 10 minutes.</p>"
            };
            message.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(_config["Email:Sender"], _config["Email:Password"]);
            await smtp.SendAsync(message);
            await smtp.DisconnectAsync(true);

            return Ok(new { success = true, message = "OTP sent to your email." });
        }

        [HttpPost("/verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = "Invalid input." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
            if (user == null)
                return NotFound(new { message = "User not found." });

            if (user.PasswordResetOtp != model.Otp || user.OtpExpiry < DateTime.UtcNow)
                return BadRequest(new { message = "Invalid or expired OTP." });

            // Optionally, clear OTP after verification
            user.PasswordResetOtp = null;
            user.OtpExpiry = null;
            await _context.SaveChangesAsync();

            await _notificationService.AddNotificationAsync(
                user.Id,
                "✅ Your OTP has been successfully verified. You may now reset your password."
            );

            return Ok(new { success = true, message = "OTP verified." });
        }

        [HttpPost("/resend-otp")]
        public async Task<IActionResult> ResendOtp([FromBody] ForgotPasswordRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = "Invalid email." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
            if (user == null)
                return NotFound(new { message = "User not found." });

            // Generate new OTP
            var random = new Random();
            var otp = random.Next(100000, 999999).ToString();
            user.PasswordResetOtp = otp;
            user.OtpExpiry = DateTime.UtcNow.AddMinutes(10);
            await _context.SaveChangesAsync();

            // Send OTP via email
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Tahi Support", _config["Email:Sender"]));
            message.To.Add(MailboxAddress.Parse(user.Email));
            message.Subject = "Your Password Reset OTP (Resent)";
            var builder = new BodyBuilder
            {
                HtmlBody = $"<p>Your new OTP is: <strong>{otp}</strong></p>" +
                           "<p>This code expires in 10 minutes.</p>"
            };
            message.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(_config["Email:Sender"], _config["Email:Password"]);
            await smtp.SendAsync(message);
            await smtp.DisconnectAsync(true);

            await _notificationService.AddNotificationAsync(
                user.Id,
                "📨 A new OTP has been sent to your email for password reset. It expires in 10 minutes."
            );

            return Ok(new { success = true, message = "OTP resent to your email." });
        }

        [HttpPost("/reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = "Invalid input." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
            if (user == null)
                return NotFound(new { message = "User not found." });

            if (BCrypt.Net.BCrypt.Verify(model.NewPassword, user.PasswordHash))
            {
                return BadRequest(new { success = false, message = "You cannot use your previous password." });
            }

            // Update password
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);

            // Clear OTP after password reset
            user.PasswordResetOtp = null;
            user.OtpExpiry = null;

            await _notificationService.AddNotificationAsync(user.Id, "Your password has been successfully changed.");

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Password reset successful." });
        }

    }
}