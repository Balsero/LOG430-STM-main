using Application.Queries.GetEarliestBus;
using Application.Queries.Seedwork;
using Application.ViewModels;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Controllers.Rest;

[ApiController]
[Route("[controller]/[action]")]
public class LeaderController : ControllerBase
{
    private readonly ILogger<FinderController> _logger;
    private readonly IQueryDispatcher _queryDispatcher;

    public LeaderController(ILogger<FinderController> logger, IQueryDispatcher queryDispatcher)
    {
        _logger = logger;
        _queryDispatcher = queryDispatcher;
    }

    [HttpGet]
    [ActionName(nameof(IsLeader))]
    [EnableRateLimiting("fixed")]
    public async Task<ActionResult<string>> IsLeader()
    {
        var isLeader = Environment.GetEnvironmentVariable("IS_LEADER");

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
        Environment.SetEnvironmentVariable("IS_LEADER", "true");
        
        _logger.LogInformation(Environment.GetEnvironmentVariable("IS_LEADER"));
        
        return Ok("Promotion to Leader success");
    }
    
    [HttpGet]
    [ActionName(nameof(Demote))]
    [EnableRateLimiting("fixed")]
    public async Task<ActionResult<string>> Demote()
    {
        Environment.SetEnvironmentVariable("IS_LEADER", "false");
        _logger.LogInformation(Environment.GetEnvironmentVariable("IS_LEADER"));
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
