using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Application.Interfaces;
using Application.Usecases;
using Microsoft.AspNetCore.RateLimiting;
using Newtonsoft.Json;
using ServiceMeshHelper;
using ServiceMeshHelper.BusinessObjects.InterServiceRequests;
using ServiceMeshHelper.BusinessObjects;
using ServiceMeshHelper.Controllers;
using System.Threading;
using System.Diagnostics.Eventing.Reader;
using ManagerSTM.Redis;
using StackExchange.Redis;

namespace RouteTimeProvider
{
    public class Program
    {
    
        public static void Main(string[] args)
        {

            // Configuration de Redis
            var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost";
            var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379";
            var redis = ConnectionMultiplexer.Connect($"{redisHost}:{redisPort}");
            var redisDb = redis.GetDatabase();
            var redisService = new RedisService(redisDb);
            redisService.TestConnection();

            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
           
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseHttpsRedirection();

            app.UseCors(
                options =>
                {
                    options.AllowAnyOrigin();
                    options.AllowAnyHeader();
                    options.AllowAnyMethod();
                }
            );

            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            var cancellationTokenSource = new CancellationTokenSource();

            app.UseCors();

            app.MapControllers();
            Task.Run(() => StartLeaderManagement(cancellationTokenSource.Token,logger, redisDb));
            app.Run();

            // Annuler le ping quand l'application se termine
            cancellationTokenSource.Cancel();
        }

        public static async Task StartLeaderManagement(CancellationToken cancellationToken, ILogger logger, IDatabase redisDb)
        {
            const string leaderLockKey = "ManagerLeaderLock";
            const int lockExpirationSeconds = 5;

            logger.LogInformation("Starting continuous Leader Management task between SideCard and SideCard2...");

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
                            bool sideCard1Alive = await IsSideCardAlive("STM.SideCard", logger);
                            bool sideCard2Alive = await IsSideCardAlive("STM2.SideCard2", logger);

                            if (sideCard1Alive && !await CheckIfOtherSideCardIsLeader("STM2.SideCard2", logger))
                            {
                                bool promotionSucceeded = await AttemptLeaderPromotion("STM.SideCard", logger);
                                if (promotionSucceeded)
                                {
                                    logger.LogInformation("STM.SideCard is now the leader.");
                                }
                            }
                            else if (sideCard2Alive && !await CheckIfOtherSideCardIsLeader("STM.SideCard", logger))
                            {
                                bool promotionSucceeded = await AttemptLeaderPromotion("STM2.SideCard2", logger);
                                if (promotionSucceeded)
                                {
                                    logger.LogInformation("STM2.SideCard2 is now the leader.");
                                }
                            }
                            else
                            {
                                logger.LogWarning("Failed to promote any SideCard to leader.");
                            }
                        }

                        // Rafraîchir le verrou pour signaler que l'instance est toujours active
                        await redisDb.LockExtendAsync(leaderLockKey, Environment.MachineName, TimeSpan.FromSeconds(lockExpirationSeconds));

                        // Délai avant le prochain rafraîchissement du verrou
                        await Task.Delay(50, cancellationToken);
                    }
                }
                finally
                {
                    // Libération du verrou en cas d'annulation
                    await redisDb.LockReleaseAsync(leaderLockKey, Environment.MachineName);
                }

                await Task.Delay(50, cancellationToken); // Délai pour éviter les promotions rapides
            }
        }

        private static async Task<bool> CheckIfLeaderExists(ILogger logger)
        {
            bool sideCard1IsLeader = await CheckIfOtherSideCardIsLeader("STM.SideCard", logger);
            bool sideCard2IsLeader = await CheckIfOtherSideCardIsLeader("STM2.SideCard2", logger);

            /*
            if (sideCard1IsLeader && sideCard2IsLeader)
            {
                // Résoudre le conflit en rétrogradant l'un des deux, ici SideCard2
                logger.LogWarning("Conflict detected: Both SideCard1 and SideCard2 are claiming leadership. Demoting SideCard2.");

                bool demotionSucceeded = await DemoteLeader("STM2.SideCard2", logger);

                if (demotionSucceeded)
                {
                    logger.LogInformation("SideCard2 has been demoted successfully. SideCard1 remains the leader.");
                    return true;
                }
                else
                {
                    logger.LogError("Failed to demote SideCard2. Manual intervention may be required.");
                    return true; // Un leader est présent malgré le conflit
                }
            }
            */

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

        
        private static async Task<bool> DemoteLeader(string targetService, ILogger logger)
        {
            try
            {
                var res = await RestController.Get(
                    new GetRoutingRequest()
                    {
                        TargetService = targetService,
                        Endpoint = "SideCard/Demote",
                        Mode = LoadBalancingMode.RoundRobin
                    });

                await foreach (var result in res!.ReadAllAsync())
                {
                    if (result.Content != null && result.Content == "Demotion success")
                    {
                        logger.LogInformation($"{targetService} has been demoted.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error demoting {targetService}. Error: {ex.Message}");
            }

            logger.LogWarning($"Failed to demote {targetService}.");
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
                logger.LogInformation($"Attempting to promote {targetService} to leader (Je suis capable d'etrer ici ?");
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