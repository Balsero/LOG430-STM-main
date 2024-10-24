using Application.Usecases;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace RouteTimeProvide.Controllers
{
    [EnableCors("AllowOrigin")]
    [ApiController]
    [Route("[controller]/[action]")]
    public class SideCardController : ControllerBase
    {
        private readonly CarTravel _carTravel;
        private readonly ILogger<SideCardController> _logger;

        public SideCardController(ILogger<SideCardController> logger)
        {
            _logger = logger;
        }
    }
}