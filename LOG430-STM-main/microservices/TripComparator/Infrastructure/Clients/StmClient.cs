using System.Net;
using Application.BusinessObjects;
using Application.DTO;
using Application.Interfaces;
using Application.Interfaces.Policies;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestSharp;
using ServiceMeshHelper;
using ServiceMeshHelper.BusinessObjects;
using ServiceMeshHelper.BusinessObjects.InterServiceRequests;
using ServiceMeshHelper.Controllers;

namespace Infrastructure.Clients;

public class StmClient : IBusInfoProvider
{
    readonly ILogger _logger;
    private readonly IBackOffRetryPolicy<StmClient> _backOffRetry;
    private readonly IInfiniteRetryPolicy<StmClient> _infiniteRetry;

    public StmClient(ILogger<StmClient> logger, IBackOffRetryPolicy<StmClient> backOffRetry, IInfiniteRetryPolicy<StmClient> infiniteRetry)
    {
        _logger = logger;
        _backOffRetry = backOffRetry;
        _infiniteRetry = infiniteRetry;
    }

    public async Task<RideDto> GetBestBus(string startingCoordinates, string destinationCoordinates)
    {
        return await _infiniteRetry.ExecuteAsync(async () =>
        {
            // Création des requêtes pour STM et STM2
            var requests = new List<GetRoutingRequest>
        {
            new GetRoutingRequest
            {
                TargetService = "STM",
                Endpoint = $"Finder/OptimalBuses",
                Params = new List<NameValue>
                {
                    new()
                    {
                        Name = "fromLatitudeLongitude",
                        Value = startingCoordinates
                    },
                    new()
                    {
                        Name = "toLatitudeLongitude",
                        Value = destinationCoordinates
                    }
                },
                Mode = LoadBalancingMode.RoundRobin
            },
            new GetRoutingRequest
            {
                TargetService = "STM2",
                Endpoint = $"Finder/OptimalBuses",
                Params = new List<NameValue>
                {
                    new()
                    {
                        Name = "fromLatitudeLongitude",
                        Value = startingCoordinates
                    },
                    new()
                    {
                        Name = "toLatitudeLongitude",
                        Value = destinationCoordinates
                    }

                },
                Mode = LoadBalancingMode.RoundRobin
            }
        };

            RideDto? leaderBusDto = null;

            // Exécuter les requêtes en parallèle en mode Round Robin
            var tasks = requests.Select(async request =>
            {
                var channel = await RestController.Get(request);
                await foreach (var res in channel.ReadAllAsync())
                {
                    if (res.StatusCode == HttpStatusCode.Forbidden) // Vérifie si l'instance n'est pas le Leader
                    {
                        // Logique pour ignorer cette réponse car l'instance n'est pas le Leader
                        _logger.LogWarning("Ignored response from non-leader instance.");
                        continue;
                    }

                    if (res.Content != null)
                    {
                        // Désérialise le contenu pour obtenir `RideDto` s'il provient du Leader
                        var busDto = JsonConvert.DeserializeObject<RideDto>(res.Content);
                        if (busDto != null)
                        {
                            return busDto; // Retourne la réponse du Leader
                        }
                    }
                }
                return null; // Retourne null si la requête échoue ou si l'instance n'est pas Leader
            });

            // Essaye de récupérer la première réponse valide du Leader
            foreach (var task in tasks)
            {
                var completedTask = await Task.WhenAny(tasks); // Attend la première tâche terminée
                leaderBusDto = await completedTask;
                if (leaderBusDto != null)
                {
                    break; // Si une réponse du Leader est trouvée, sort de la boucle
                }

                // Si la tâche échoue, elle est retirée de la liste des tâches
                tasks = tasks.Where(t => t != completedTask);
            }

            if (leaderBusDto == null) throw new Exception("No valid leader response received from STM or STM2");

            return leaderBusDto;
        });
    }

    public Task BeginTracking(RideDto stmBus)
    {
        return _infiniteRetry.ExecuteAsync(async () =>
        {
            _ = await RestController.Post(new PostRoutingRequest<RideDto>()
            {
                TargetService = "STM2",
                Endpoint = $"Track/BeginTracking",
                Payload = stmBus,
                Mode = LoadBalancingMode.RoundRobin
            });
        });
    }

    public Task<IBusTracking?> GetTrackingUpdate()
    {
        return _infiniteRetry.ExecuteAsync<IBusTracking?>(async () =>
        {
            var channel = await RestController.Get(new GetRoutingRequest()
            {
                TargetService = "STM2",
                Endpoint = $"Track/GetTrackingUpdate",
                Params = new List<NameValue>(),
                Mode = LoadBalancingMode.RoundRobin
            });

            RestResponse? data = null;

            await foreach (var res in channel.ReadAllAsync())
            {
                data = res;

                break;
            }

            if (data is null || !data.IsSuccessStatusCode || data.StatusCode.Equals(HttpStatusCode.NoContent)) return null;

            var busTracking = JsonConvert.DeserializeObject<BusTracking>(data.Content!);

            return busTracking;

        });
    }
}