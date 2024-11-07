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
            // Liste des requêtes pour STM et STM2
            var requests = new List<GetRoutingRequest>
        {
            new GetRoutingRequest
            {
                TargetService = "STM",
                Endpoint = $"Finder/OptimalBuses",
                Params = new List<NameValue>
                {
                    new NameValue { Name = "fromLatitudeLongitude", Value = startingCoordinates },
                    new NameValue { Name = "toLatitudeLongitude", Value = destinationCoordinates }
                }
            },
            new GetRoutingRequest
            {
                TargetService = "STM2",
                Endpoint = $"Finder/OptimalBuses",
                Params = new List<NameValue>
                {
                    new NameValue { Name = "fromLatitudeLongitude", Value = startingCoordinates },
                    new NameValue { Name = "toLatitudeLongitude", Value = destinationCoordinates }
                }
            }
        };

            RideDto? leaderBusDto = null;

            // Traite une requête à la fois
            foreach (var request in requests)
            {
                try
                {
                    var channel = await RestController.Get(request);
                    await foreach (var res in channel.ReadAllAsync())
                    {
                        if (res.StatusCode == HttpStatusCode.Forbidden) // Vérifie si l'instance n'est pas le Leader
                        {
                            _logger.LogWarning("Ignored response from non-leader instance.");
                            break; // Ignorer cette instance et passer à la suivante
                        }

                        if (res.Content != null)
                        {
                            leaderBusDto = JsonConvert.DeserializeObject<RideDto>(res.Content);
                            if (leaderBusDto != null)
                            {
                                return leaderBusDto; // Retourne la première réponse valide
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error while processing request for {request.TargetService}");
                    continue; // Si une requête échoue, passer à la suivante
                }
            }

            if (leaderBusDto == null)
                throw new Exception("No valid leader response received from STM or STM2");

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