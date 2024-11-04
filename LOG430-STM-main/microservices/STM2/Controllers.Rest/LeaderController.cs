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
        var isLeader = Environment.GetEnvironmentVariable("IS_LEADER_2");

        if (isLeader == "true")
        {
            return Ok("isLeader");
        }

        return Problem("NotLeader");
    }

    [HttpGet]
    [ActionName(nameof(LeaderPromotion))]
    [EnableRateLimiting("fixed")]
    public async Task<ActionResult<string>> LeaderPromotion()
    {
        Environment.SetEnvironmentVariable("IS_LEADER_2", "true");
        return Ok("Promotion to Leader success");
    }
}
