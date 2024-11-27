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
            Console.WriteLine($"Connecting to Redis at {redisHost}:{redisPort}");
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
            const int lockExpirationSeconds = 2;

            logger.LogInformation("Starting continuous Leader Management task between TripComparator and TripComparator2");

            while (!cancellationToken.IsCancellationRequested)
            {
                // Essayer d'acquérir le verrou Redis
                bool lockAcquired = await redisDb.LockTakeAsync(leaderLockKey, Environment.MachineName, TimeSpan.FromSeconds(lockExpirationSeconds));

                if (lockAcquired)
                {
                    try
                    {
                        // Leader actuel prend la tâche et renouvelle régulièrement le verrou
                        logger.LogInformation($"{Environment.MachineName} est le Leader et va gérer l'assignation des TripComparator.");

                        while (!cancellationToken.IsCancellationRequested)
                        {
                            // Logique pour gérer les TripComparator
                            bool leaderAssigned = await CheckIfLeaderExists(logger);

                            if (!leaderAssigned)
                            {
                                bool tripComparatorAlive = await IsTripComparatorAlive("TripComparator", logger);
                                bool tripComparator2Alive = await IsTripComparatorAlive("TripComparator2", logger);

                                if (tripComparatorAlive && !await CheckIfOtherTripComparatorIsLeader("TripComparator2", logger))
                                {
                                    bool promotionSucceeded = await AttemptLeaderPromotion("TripComparator", logger);
                                    if (promotionSucceeded)
                                    {
                                        logger.LogInformation("TripComparator is now the leader.");
                                    }
                                }
                                else if (tripComparator2Alive && !await CheckIfOtherTripComparatorIsLeader("TripComparator", logger))
                                {
                                    bool promotionSucceeded = await AttemptLeaderPromotion("TripComparator2", logger);
                                    if (promotionSucceeded)
                                    {
                                        logger.LogInformation("TripComparator2 is now the leader.");
                                    }
                                }
                                else
                                {
                                    logger.LogWarning("Failed to promote any TripComparator to leader.");
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
            bool tripComparatorIsLeader = await CheckIfOtherTripComparatorIsLeader("TripComparator", logger);
            bool tripComparator2IsLeader = await CheckIfOtherTripComparatorIsLeader("TripComparator2", logger);

            if (tripComparatorIsLeader || tripComparator2IsLeader)
            {
                logger.LogInformation("A leader is already assigned.");
                return true;
            }

            logger.LogInformation("No leader is currently assigned.");
            return false;
        }

        private static async Task<bool> CheckIfOtherTripComparatorIsLeader(string targetService, ILogger logger)
        {
            try
            {
                var res = await RestController.Get(
                    new GetRoutingRequest()
                    {
                        TargetService = targetService,
                        Endpoint = $"Leader/IsLeader",
                        Mode = LoadBalancingMode.RoundRobin
                    });

                await foreach (var result in res!.ReadAllAsync())
                {
                    if (result.Content != null && JsonConvert.DeserializeObject<string>(result.Content) == "isLeader")
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

        private static async Task<bool> IsTripComparatorAlive(string targetService, ILogger logger)
        {
            try
            {
                logger.LogInformation($"Attempting to ping {targetService}.");

                var res = await RestController.Get(
                    new GetRoutingRequest()
                    {
                        TargetService = targetService,
                        Endpoint = "Leader/isAlive",
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
                        Endpoint = "Leader/LeaderPromotion",
                        Mode = LoadBalancingMode.RoundRobin
                    });

                await foreach (var result in res!.ReadAllAsync())
                {
                    if (result.Content != null && result.Content == "Promotion to Leader success")
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
