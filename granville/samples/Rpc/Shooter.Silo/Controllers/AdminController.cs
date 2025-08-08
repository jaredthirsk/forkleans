using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Orleans;
using System.Threading.Tasks;

namespace Shooter.Silo.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly IClusterClient _clusterClient;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            IHostApplicationLifetime applicationLifetime,
            IClusterClient clusterClient,
            ILogger<AdminController> logger)
        {
            _applicationLifetime = applicationLifetime;
            _clusterClient = clusterClient;
            _logger = logger;
        }

        /// <summary>
        /// Gracefully shuts down the silo and notifies all connected services.
        /// </summary>
        [HttpPost("shutdown")]
        public IActionResult Shutdown([FromQuery] int delaySeconds = 5)
        {
            _logger.LogWarning("Graceful shutdown requested with {DelaySeconds} second delay", delaySeconds);

            // Notify all action servers to prepare for shutdown
            try
            {
                // TODO: Implement grain notification to action servers
                _logger.LogInformation("Notifying action servers of impending shutdown");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying action servers");
            }

            // Schedule the shutdown
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                _logger.LogWarning("Executing graceful shutdown now");
                _applicationLifetime.StopApplication();
            });

            return Ok(new 
            { 
                message = "Graceful shutdown initiated",
                delaySeconds = delaySeconds,
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Gets the health status of the silo.
        /// </summary>
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                uptime = DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime
            });
        }

        /// <summary>
        /// Triggers garbage collection (useful for memory issue debugging).
        /// </summary>
        [HttpPost("gc")]
        public IActionResult TriggerGC()
        {
            var before = GC.GetTotalMemory(false);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var after = GC.GetTotalMemory(false);

            return Ok(new
            {
                memoryBeforeBytes = before,
                memoryAfterBytes = after,
                freedBytes = before - after,
                timestamp = DateTime.UtcNow
            });
        }
    }
}