using System.Net;
using Application;
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
        var redisDb = RedisConnectionManager.GetDatabase();

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

            await redisDb.StringSetAsync("TripComparator:CurrentState", "GetBestBus");
            return leaderBusDto;
        });
    }


    public async Task BeginTracking(RideDto stmBus)
    {
        var redisDb = RedisConnectionManager.GetDatabase();

        await _infiniteRetry.ExecuteAsync(async () =>
        {
            // Liste des requêtes pour STM et STM2
            var requests = new List<PostRoutingRequest<RideDto>>
        {
            new PostRoutingRequest<RideDto>
            {
                TargetService = "STM",
                Endpoint = $"Track/BeginTracking",
                Payload = stmBus,
                Mode = LoadBalancingMode.RoundRobin
            },
            new PostRoutingRequest<RideDto>
            {
                TargetService = "STM2",
                Endpoint = $"Track/BeginTracking",
                Payload = stmBus,
                Mode = LoadBalancingMode.RoundRobin
            }
        };

            foreach (var request in requests)
            {
                try
                {
                    // Envoyer la requête POST à l'instance et lire la réponse
                    var channel = await RestController.Post(request);

                    await foreach (var res in channel.ReadAllAsync())
                    {
                        // Vérifier si la réponse contient un message de non-Leader
                        if (res.StatusCode == HttpStatusCode.Forbidden) // Vérifie le code de statut
                        {
                            _logger.LogWarning($"Tracking rejected by non-leader instance {request.TargetService}.");
                            continue; // Passer à l'autre instance
                        }

                        // Log succès
                        _logger.LogInformation($"Tracking started successfully on {request.TargetService}.");
                        await redisDb.StringSetAsync("TripComparator:CurrentState", "BeginTracking");

                        return; // Succès, arrêter la méthode
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error while trying to start tracking on {request.TargetService}");
                    continue; // Passer à l'autre instance en cas d'erreur
                }
            }

            // Si aucune requête n'a réussi
            throw new Exception("Tracking could not be started on either STM or STM2");
        });
    }

    public Task<IBusTracking?> GetTrackingUpdate()
    {
        var redisDb = RedisConnectionManager.GetDatabase();
        

        return _infiniteRetry.ExecuteAsync<IBusTracking?>(async () =>
        {
            // Liste des requêtes pour STM et STM2
            var requests = new List<GetRoutingRequest>
        {
            new GetRoutingRequest
            {
                TargetService = "STM",
                Endpoint = $"Track/GetTrackingUpdate",
                Params = new List<NameValue>(),
                Mode = LoadBalancingMode.RoundRobin
            },
            new GetRoutingRequest
            {
                TargetService = "STM2",
                Endpoint = $"Track/GetTrackingUpdate",
                Params = new List<NameValue>(),
                Mode = LoadBalancingMode.RoundRobin
            }
        };

            RestResponse? validData = null;

            foreach (var request in requests)
            {
                try
                {
                    var channel = await RestController.Get(request);
                    await foreach (var res in channel.ReadAllAsync())
                    {
                        if (res.StatusCode == HttpStatusCode.Forbidden) // Vérifie si l'instance n'est pas Leader
                        {
                            _logger.LogWarning($"Request rejected by non-leader instance {request.TargetService}.");
                            continue; // Passer à l'autre instance
                        }

                        // Si la réponse est valide, la traiter
                        if (res != null && res.IsSuccessStatusCode && !res.StatusCode.Equals(HttpStatusCode.NoContent))
                        {
                            validData = res;
                            break; // Sortir dès qu'une réponse valide est trouvée
                        }
                    }

                    if (validData != null)
                        break; // Stopper la boucle dès qu'une réponse valide est trouvée
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error while processing GetTrackingUpdate for {request.TargetService}");
                    continue; // Passer à l'autre instance en cas d'erreur
                }
            }

            if (validData == null) return null; // Si aucune réponse valide n'a été trouvée

            // Désérialiser la réponse en objet IBusTracking
            var busTracking = JsonConvert.DeserializeObject<BusTracking>(validData.Content!);


            await redisDb.StringSetAsync("TripComparator:CurrentState", "TrackingComplete");
            return busTracking;
        });
    }
}