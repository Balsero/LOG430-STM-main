using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using ManagerSTM.Redis;
using Newtonsoft.Json;
using ServiceMeshHelper.BusinessObjects.InterServiceRequests;
using ServiceMeshHelper.Controllers;
using ServiceMeshHelper;

namespace RouteTimeProvider
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Configuration de Redis
            var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost";
            var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379";
            var redis = ConnectionMultiplexer.Connect($"{redisHost}:{redisPort}");
            var redisDb = redis.GetDatabase();
            var redisService = new RedisService(redisDb);
            redisService.TestConnection();

            // Configuration de l'application ASP.NET
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            var app = builder.Build();

            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseHttpsRedirection();

            app.UseCors(options =>
            {
                options.AllowAnyOrigin();
                options.AllowAnyHeader();
                options.AllowAnyMethod();
            });

            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            var cancellationTokenSource = new CancellationTokenSource();

            // Lancer la gestion du Leader
            Task.Run(() => StartLeaderManagement(cancellationTokenSource.Token, logger, redisDb));

            app.UseCors();
            app.MapControllers();
            app.Run();
            cancellationTokenSource.Cancel();
        }

        // Tâche de gestion du Leader
        public static async Task StartLeaderManagement(CancellationToken cancellationToken, ILogger logger, IDatabase redisDb)
        {
            const string leaderLockKey = "ManagerTCLeaderLock";
            // Durée de vie du verrou en secondes songez changer cette valeur
            const int lockExpirationSeconds = 5;

            logger.LogInformation("Starting continuous Leader Management task between SideCard and SideCard2...");

            while (!cancellationToken.IsCancellationRequested)
            {
                // Essayer d'acquérir le verrou Redis
                bool lockAcquired = await redisDb.LockTakeAsync(leaderLockKey, Environment.MachineName, TimeSpan.FromSeconds(lockExpirationSeconds));

                if (lockAcquired)
                {
                    try
                    {
                        // Leader actuel prend la tâche et renouvelle régulièrement le verrou
                        logger.LogInformation($"{Environment.MachineName} est le Leader et va gérer l'assignation des SideCards.");

                        while (!cancellationToken.IsCancellationRequested)
                        {
                            // Logique pour gérer les SideCards
                            bool leaderAssigned = await CheckIfLeaderExists(logger);

                            if (!leaderAssigned)
                            {
                                bool sideCard1Alive = await IsSideCardAlive("TripComparator.SideCard", logger);
                                bool sideCard2Alive = await IsSideCardAlive("TripComparator2.SideCard2", logger);

                                if (sideCard1Alive && !await CheckIfOtherSideCardIsLeader("TripComparator2.SideCard2", logger))
                                {
                                    bool promotionSucceeded = await AttemptLeaderPromotion("TripComparator.SideCard", logger);
                                    if (promotionSucceeded)
                                    {
                                        logger.LogInformation("TripComparator.SideCard is now the leader.");
                                    }
                                }
                                else if (sideCard2Alive && !await CheckIfOtherSideCardIsLeader("TripComparator.SideCard", logger))
                                {
                                    bool promotionSucceeded = await AttemptLeaderPromotion("TripComparator2.SideCard2", logger);
                                    if (promotionSucceeded)
                                    {
                                        logger.LogInformation("TripComparator2.SideCard2 is now the leader.");
                                    }
                                }
                                else
                                {
                                    logger.LogWarning("Failed to promote any SideCard to leader.");
                                }
                            }

                            // Rafraîchir le verrou pour signaler que l'instance est toujours active
                            bool lockExtended = await redisDb.LockExtendAsync(leaderLockKey, Environment.MachineName, TimeSpan.FromSeconds(lockExpirationSeconds));
                            if (!lockExtended)
                            {
                                logger.LogWarning($"{Environment.MachineName} a perdu le verrou de leadership. Un autre instance peut maintenant devenir le Leader.");
                                break;
                            }

                            // Délai avant le prochain rafraîchissement du verrou
                            await Task.Delay(20, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Erreur lors de l'extension ou de la libération du verrou.");
                        await redisDb.LockReleaseAsync(leaderLockKey, Environment.MachineName); // Libération propre
                    }
                }
                else
                {
                    logger.LogInformation("Un autre ManagerTC est actuellement Leader. Attente avant de réessayer...");
                }

                // Attendre avant de réessayer pour éviter une charge excessive sur Redis
                await Task.Delay(50, cancellationToken);
            }
        }

        private static async Task<bool> CheckIfLeaderExists(ILogger logger)
        {
            bool sideCard1IsLeader = await CheckIfOtherSideCardIsLeader("TripComparator.SideCard", logger);
            bool sideCard2IsLeader = await CheckIfOtherSideCardIsLeader("TripComparator2.SideCard2", logger);

            if (sideCard1IsLeader || sideCard2IsLeader)
            {
                logger.LogInformation("A leader is already assigned.");
                return true;
            }

            logger.LogInformation("No leader is currently assigned.");
            return false;
        }

        private static async Task<bool> CheckIfOtherSideCardIsLeader(string targetService, ILogger logger)
        {
            try
            {
                var res = await RestController.Get(
                    new GetRoutingRequest()
                    {
                        TargetService = targetService,
                        Endpoint = $"SideCard/CheckIfLeader",
                        Mode = LoadBalancingMode.RoundRobin
                    });

                await foreach (var result in res!.ReadAllAsync())
                {
                    if (result.Content != null && JsonConvert.DeserializeObject<string>(result.Content) == "Pod leader is confirmed as the leader.")
                    {
                        logger.LogInformation($"{targetService} is currently the leader.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error checking leader status for {targetService}. Error: {ex.Message}");
            }

            return false;
        }

        private static async Task<bool> IsSideCardAlive(string targetService, ILogger logger)
        {
            try
            {
                logger.LogInformation($"Attempting to ping {targetService}.");

                var res = await RestController.Get(
                    new GetRoutingRequest()
                    {
                        TargetService = targetService,
                        Endpoint = "SideCard/isAlive",
                        Mode = LoadBalancingMode.Broadcast
                    });

                if (res == null)
                {
                    logger.LogWarning($"Routing request returned null for target service: {targetService}");
                    return false;
                }

                await foreach (var result in res.ReadAllAsync())
                {
                    if (JsonConvert.DeserializeObject<string>(result.Content) == "isAlive")
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error checking if {targetService} is alive. Error: {ex.Message}");
            }

            return false;
        }

        private static async Task<bool> AttemptLeaderPromotion(string targetService, ILogger logger)
        {
            try
            {
                logger.LogInformation($"Attempting to promote {targetService} to leader.");
                var res = await RestController.Get(
                    new GetRoutingRequest()
                    {
                        TargetService = targetService,
                        Endpoint = "SideCard/PromoteToLeader",
                        Mode = LoadBalancingMode.RoundRobin
                    });

                await foreach (var result in res!.ReadAllAsync())
                {
                    if (result.Content != null && result.Content == "Promoted to leader")
                    {
                        logger.LogInformation($"{targetService} has been promoted to leader.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error promoting {targetService} to leader. Error: {ex.Message}");
            }

            logger.LogWarning($"Failed to promote {targetService} to leader.");
            return false;
        }
    }
}
