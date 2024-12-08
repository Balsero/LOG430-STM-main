
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Controllers.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class LeaderController : ControllerBase
{
    private readonly ILogger<LeaderController> _logger;

    public LeaderController(ILogger<LeaderController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    [ActionName(nameof(IsLeader))]
    [EnableRateLimiting("fixed")]
    public async Task<ActionResult<string>> IsLeader()
    {
        var isLeader = Environment.GetEnvironmentVariable("IS_LEADER_TC");

        if (isLeader == "true")
        {
            return Ok("isLeader");
        }
        else
        {
            return Ok("NotLeader");
        }
    }
    [HttpGet]
    [ActionName(nameof(LeaderPromotion))]
    [EnableRateLimiting("fixed")]
    public async Task<ActionResult<string>> LeaderPromotion()
    {
        Environment.SetEnvironmentVariable("IS_LEADER_TC", "true");
        
        _logger.LogInformation(Environment.GetEnvironmentVariable("IS_LEADER_TC"));
        
        return Ok("Promotion to Leader success");
    }

    [HttpGet]
    [ActionName(nameof(Demote))]
    [EnableRateLimiting("fixed")]
    public async Task<ActionResult<string>> Demote()
    {
        Environment.SetEnvironmentVariable("IS_LEADER_TC", "false");
        _logger.LogInformation(Environment.GetEnvironmentVariable("IS_LEADER_TC"));
        return Ok("Demotion success");
    }

    [HttpGet]
    [ActionName(nameof(isAlive))]
    [EnableRateLimiting("fixed")]
    public async Task<ActionResult<string>> isAlive()
    {

        return Ok("isAlive");
    }
}
