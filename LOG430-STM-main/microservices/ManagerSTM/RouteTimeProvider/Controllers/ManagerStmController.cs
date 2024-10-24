using Application.Usecases;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace RouteTimeProvider.Controllers
{
    [EnableCors("AllowOrigin")]
    [ApiController]
    [Route("[controller]/[action]")]
    public class ManagerStmController : ControllerBase
    {
        private readonly CarTravel _carTravel;
        private readonly ILogger<ManagerStmController> _logger;

        public ManagerStmController(CarTravel carTravel, ILogger<ManagerStmController> logger)
        {
            _carTravel = carTravel;
            _logger = logger;
        }

        [HttpGet]
        [ActionName(nameof(Get))]
        [EnableRateLimiting("fixed")]
        public async Task<ActionResult<int>> Get(string startingCoordinates, string destinationCoordinates)
        {
            _logger.LogInformation($"Fetching car travel time from {startingCoordinates} to {destinationCoordinates}");

            var travelTime = await _carTravel.GetTravelTimeInSeconds(startingCoordinates, destinationCoordinates);

            return Ok(travelTime);
        }

        [HttpGet]
        [ActionName(nameof(isAlive))]
        [EnableRateLimiting("fixed")]
        public async Task<ActionResult<string>> isAlive()
        {

            return Ok("isAlive");
        }
    }
}