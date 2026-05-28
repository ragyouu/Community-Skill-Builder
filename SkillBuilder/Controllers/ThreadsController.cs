using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillBuilder.Data;

namespace SkillBuilder.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ThreadsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ThreadsController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Add threads to a user's account (payment processing dummy)
        /// </summary>
        [HttpPost("add")]
        public async Task<IActionResult> AddThreads([FromBody] AddThreadsRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.UserId) || request.ThreadsToAdd <= 0)
            {
                return BadRequest(new { success = false, message = "Invalid request parameters" });
            }

            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.UserId);
                if (user == null)
                {
                    return NotFound(new { success = false, message = "User not found" });
                }

                // Validate minimum threads (100)
                if (request.ThreadsToAdd < 100)
                {
                    return BadRequest(new { success = false, message = "Minimum purchase is 100 threads" });
                }

                // Add threads to user (dummy payment - just add directly)
                user.Threads += request.ThreadsToAdd;

                // Save to database
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = $"Successfully added {request.ThreadsToAdd} threads",
                    newThreadsTotal = user.Threads,
                    transactionId = Guid.NewGuid().ToString()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while processing your request",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get current threads balance for a user
        /// </summary>
        [HttpGet("{userId}")]
        public async Task<IActionResult> GetThreadsBalance(string userId)
        {
            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    return NotFound(new { success = false, message = "User not found" });
                }

                return Ok(new
                {
                    success = true,
                    userId = user.Id,
                    threads = user.Threads
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }

    /// <summary>
    /// Request model for adding threads
    /// </summary>
    public class AddThreadsRequest
    {
        public string UserId { get; set; }
        public int ThreadsToAdd { get; set; }
        public string PaymentMethod { get; set; } // "ewallet" or "creditcard"
    }
}
