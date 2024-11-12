using Application.Usecases;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServiceMeshHelper.BusinessObjects.InterServiceRequests;
using ServiceMeshHelper;
using ServiceMeshHelper.Controllers;
using Newtonsoft.Json;

namespace RouteTimeProvide.Controllers
{
    [EnableCors("AllowOrigin")]
    [ApiController]
    [Route("[controller]/[action]")]
    public class SideCardController : ControllerBase
    {
        private readonly ILogger<SideCardController> _logger;
        private readonly IServiceProvider _services; 

        public SideCardController(ILogger<SideCardController> logger, IServiceProvider services)
        {
            _logger = logger;
            _services = services;
        }

        [HttpGet]
        [ActionName(nameof(isAlive))]
        [EnableRateLimiting("fixed")]
        public async Task<ActionResult<string>> isAlive()
        {

            return Ok("isAlive");
        }

        [HttpGet]
        [ActionName(nameof(PromoteToLeader))]
        [EnableRateLimiting("fixed")]
        public async Task<ActionResult<string>> PromoteToLeader()
        {
            var podLeaderID = await ServiceMeshInfoProvider.PodLeaderId;

            try
            {
                var res = await RestController.Get(
                    new GetRoutingRequest()
                    {
                        TargetService = podLeaderID,
                        Endpoint = "Leader/LeaderPromotion",
                        Mode = LoadBalancingMode.Broadcast // Assurez-vous que ce mode est pris en charge
                    });
            }

            catch (Exception e)
            {
                _logger.LogError(e, "Error promoting to leader");
                return StatusCode(500, "Error promoting to leader");
            }

            return Ok("Promoted to leader");
        }

        [HttpGet]
        [ActionName(nameof(CheckIfLeader))]
        [EnableRateLimiting("fixed")]
        public async Task<IActionResult> CheckIfLeader()
        {
            _logger.LogInformation("Starting CheckIfLeader endpoint...");

            const int pingIntervalMs = 50; // Intervalle de ping en millisecondes

            // Obtenez l'ID du pod leader
            var podLeaderID = await ServiceMeshInfoProvider.PodLeaderId;
            _logger.LogInformation($"Pod Leader ID: {podLeaderID}");

            try
            {
                // Utilisez HttpContext.RequestAborted comme CancellationToken
                var cancellationToken = HttpContext.RequestAborted;

                // Appeler la route 'isLeader' en mode Broadcast
                var res = await RestController.Get(
                    new GetRoutingRequest()
                    {
                        TargetService = podLeaderID,
                        Endpoint = $"Leader/IsLeader",
                        Mode = LoadBalancingMode.Broadcast // Utiliser Broadcast pour le ping
                    });

                // Itérer sur les résultats asynchrones
                await foreach (var result in res!.ReadAllAsync(cancellationToken))
                {
                    // Vérifier la réponse
                    if (result.Content != null && JsonConvert.DeserializeObject<string>(result.Content) == "isLeader")
                    {
                        _logger.LogInformation("Pod leader confirmed as leader.");
                        return Ok("Pod leader is confirmed as the leader.");
                    }
                    else
                    {
                        _logger.LogInformation("Pod leader is not the leader.");
                        return Problem("Pod leader is not the leader.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Service is not available. Detailed error: {ex.GetType().Name} - {ex.Message}");
                return StatusCode(500, "Service is not available");
            }

            // Si aucune réponse n'est reçue, attendre et essayer à nouveau (en boucle)
            await Task.Delay(pingIntervalMs, HttpContext.RequestAborted);

            // En cas d'absence de réponse, retournez une réponse par défaut
            return StatusCode(500, "No response from the leader check.");
        }
    }
}

