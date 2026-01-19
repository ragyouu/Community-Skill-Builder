using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using QuestPDF.Infrastructure;
using SkillBuilder.Data;
using SkillBuilder.Models;
using SkillBuilder.Services;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<ICloudinaryService, CloudinaryService>();

builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();

builder.Services.AddScoped<IAchievementService, AchievementService>();

builder.Services.AddHostedService<WeeklyLeaderboardRewardService>();

builder.Services.AddScoped<ICommunityAnalyticsService, CommunityAnalyticsService>();

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 200 * 1024 * 1024; // 200 MB
});

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AddServerHeader = false; // Prevents "Server: Kestrel" header
    serverOptions.Limits.MaxRequestBodySize = 200 * 1024 * 1024; // 200 MB
});


// Get connection string from appsettings.json
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Add DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IEmailService, SkillBuilder.Services.EmailService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<CertificateService>();

builder.Services.AddAntiforgery(options =>
{
    // Fixes "Cookie Without Secure Flag" for Antiforgery
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.HttpOnly = true; // Extra security layer
    options.Cookie.SameSite = SameSiteMode.Strict; // Recommended for CSRF
});

builder.Services.AddAuthentication("TahiAuth")
    .AddCookie("TahiAuth", options =>
    {
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.HttpOnly = true;
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);

        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

                if (userId != null)
                {
                    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);

                    if (user == null || user.IsArchived)
                    {
                        // Reject session if user is archived or missing
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync("TahiAuth");
                    }
                }
            }
        };
    });


QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

// Middleware pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self'; " +
        "font-src 'self'; " +
        "frame-ancestors 'none'; " +
        "object-src 'none';");

    // Fix: Missing Anti-clickjacking Header
    context.Response.Headers.Append("X-Frame-Options", "DENY");

    // Fix: X-Content-Type-Options Header Missing
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");

    await next();
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();