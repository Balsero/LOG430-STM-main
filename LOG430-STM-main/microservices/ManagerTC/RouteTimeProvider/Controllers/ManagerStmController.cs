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
       
        private readonly ILogger<ManagerStmController> _logger;

        public ManagerStmController(ILogger<ManagerStmController> logger)
        {
            
            _logger = logger;
        }
    }
}