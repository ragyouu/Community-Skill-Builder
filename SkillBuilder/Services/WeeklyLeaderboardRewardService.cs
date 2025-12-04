using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SkillBuilder.Data;
using SkillBuilder.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class WeeklyLeaderboardRewardService : IHostedService, IDisposable
{
    private Timer _timer;
    private readonly IServiceScopeFactory _scopeFactory;

    public WeeklyLeaderboardRewardService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.Now;

        // Calculate days until next Monday
        int daysUntilMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;

        // If today is Monday but time already passed 00:00 → schedule next week
        if (daysUntilMonday == 0 && now.TimeOfDay > TimeSpan.Zero)
            daysUntilMonday = 7;

        // Next Monday at 00:00
        var nextRun = now.Date.AddDays(daysUntilMonday);

        var initialDelay = nextRun - now;

        _timer = new Timer(DoWork, null, initialDelay, TimeSpan.FromDays(7));

        return Task.CompletedTask;
    }

    private async void DoWork(object state)
    {
        using var scope = _scopeFactory.CreateScope();

        // Resolve scoped services inside the scope
        var _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var topUsers = _context.Users
            .Where(u => u.Role == "Learner")
            .OrderByDescending(u => u.Points)
            .Take(3)
            .ToList();

        for (int i = 0; i < topUsers.Count; i++)
        {
            int reward = i switch
            {
                0 => 50,
                1 => 30,
                2 => 10,
                _ => 0
            };

            topUsers[i].Threads += reward;

            // Send notification
            string message = i switch
            {
                0 => $"🥇 Congratulations Top 1! You earned 50 Threads this week!",
                1 => $"🥈 Congratulations Top 2! You earned 30 Threads this week!",
                2 => $"🥉 Congratulations Top 3! You earned 10 Threads this week!",
                _ => ""
            };
            await notificationService.AddNotificationAsync(topUsers[i].Id, message);
        }

        await _context.SaveChangesAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();
}