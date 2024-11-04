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

namespace RouteTimeProvider
{
    public class Program
    {
    
        public static void Main(string[] args)
        {

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
            Task.Run(() => StartLeaderManagement(cancellationTokenSource.Token,logger));
            app.Run();

            // Annuler le ping quand l'application se termine
            cancellationTokenSource.Cancel();
        }

        public static async Task StartLeaderManagement(CancellationToken cancellationToken, ILogger logger)
        {
            logger.LogInformation("Starting continuous Leader Management task between SideCard and SideCard2...");

            while (!cancellationToken.IsCancellationRequested)
            {
                bool leaderAssigned = await CheckIfLeaderExists(logger);

                if (!leaderAssigned)
                {
                    logger.LogInformation("No leader assigned. Attempting to promote a leader...");

                    bool sideCard1Alive = await IsSideCardAlive("STM.SideCard", logger);
                    bool sideCard2Alive = await IsSideCardAlive("STM2.SideCard2", logger);

                    if (!sideCard1Alive && !sideCard2Alive)
                    {
                        logger.LogWarning("Both SideCards are down. Cannot assign a leader.");
                    }
                    else
                    {
                        // Tentative de promotion avec vérification stricte
                        bool promotionSucceeded = false;

                        if (sideCard1Alive && !await CheckIfOtherSideCardIsLeader("STM2.SideCard2", logger))
                        {
                            if (!await CheckIfLeaderExists(logger))
                            {
                                promotionSucceeded = await AttemptLeaderPromotion("STM.SideCard", logger);
                                if (promotionSucceeded)
                                {
                                    logger.LogInformation("STM.SideCard is now the leader.");
                                }
                            }
                        }

                        if (!promotionSucceeded && sideCard2Alive && !await CheckIfOtherSideCardIsLeader("STM.SideCard", logger))
                        {
                            if (!await CheckIfLeaderExists(logger))
                            {
                                promotionSucceeded = await AttemptLeaderPromotion("STM2.SideCard2", logger);
                                if (promotionSucceeded)
                                {
                                    logger.LogInformation("SideCard2 is now the leader.");
                                }
                            }
                        }

                        if (!promotionSucceeded)
                        {
                            logger.LogWarning("Failed to promote any SideCard to leader.");
                        }
                    }
                }
                else
                {
                    logger.LogInformation("A leader is already assigned.");
                }

                await Task.Delay(1000, cancellationToken); // Délai entre chaque boucle pour éviter la promotion rapide
            }
        }

        private static async Task<bool> CheckIfLeaderExists(ILogger logger)
        {
            bool sideCard1IsLeader = await CheckIfOtherSideCardIsLeader("STM.SideCard", logger);
            bool sideCard2IsLeader = await CheckIfOtherSideCardIsLeader("STM2.SideCard2", logger);

            if (sideCard1IsLeader && sideCard2IsLeader)
            {
                // Resolve conflict by demoting one of them. Let's demote SideCard2 in this case.
                logger.LogWarning("Conflict detected: Both SideCard1 and SideCard2 are claiming leadership. Demoting SideCard2.");

                // Attempt to demote SideCard2 by calling a demotion endpoint or setting the environment variable to "false".
                bool demotionSucceeded = await DemoteLeader("STM2.SideCard2", logger);

                if (demotionSucceeded)
                {
                    logger.LogInformation("SideCard2 has been demoted successfully. SideCard1 remains the leader.");
                    return true;
                }
                else
                {
                    logger.LogError("Failed to demote SideCard2. Manual intervention may be required.");
                    return true; // Return true to indicate there's a leader, but a conflict persists.
                }
            }

            if (sideCard1IsLeader || sideCard2IsLeader)
            {
                logger.LogInformation("A leader is already assigned.");
                return true;
            }

            logger.LogInformation("No leader is currently assigned.");
            return false;
        }

        // Méthode pour vérifier si l'autre SideCard est déjà leader
        private static async Task<bool> CheckIfOtherSideCardIsLeader(string targetService, ILogger logger)
        {
            try
            {
                var res = await RestController.Get(
                    new GetRoutingRequest()
                    {
                        TargetService = targetService,
                        Endpoint = $"SideCard/isLeader",
                        Mode = LoadBalancingMode.Broadcast
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

        private static async Task<bool> DemoteLeader(string targetService, ILogger logger)
        {
            try
            {
                var res = await RestController.Get(
                    new GetRoutingRequest()
                    {
                        TargetService = targetService,
                        Endpoint = "Leader/Demote",
                        Mode = LoadBalancingMode.Broadcast // Ensure this mode is supported
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

        // <summary>
        /// Vérifie si un leader est actuellement assigné en interrogeant les deux SideCards.
        /// </summary>
        private static async Task<bool> IsLeaderAssigned(ILogger logger)
        {
            // Vérifiez si SideCard ou SideCard2 est déjà le leader
            bool isSideCard1Leader = await IsSideCardLeader("STM.SideCard",logger);
            bool isSideCard2Leader = await IsSideCardLeader("STM2.SideCard2", logger);

            if (isSideCard1Leader || isSideCard2Leader)
            {
                logger.LogInformation("A leader is already assigned.");
                return true;
            }

            logger.LogInformation("No leader is currently assigned.");
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
                        Mode = LoadBalancingMode.Broadcast // Assurez-vous que ce mode est pris en charge
                    });

                if (res == null)
                {
                    logger.LogWarning($"Routing request returned null for target service: {targetService}");
                    return false;
                }

                await foreach (var result in res.ReadAllAsync())
                {
                    logger.LogInformation($"Response from {targetService}: {result.Content}");

                    if (JsonConvert.DeserializeObject<string>(result.Content) == "isAlive")
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error checking if {targetService} is alive. Detailed error: {ex.GetType().Name} - {ex.Message}");
            }

            logger.LogWarning($"{targetService} is not alive.");
            return false;
        }

        private static async Task<bool> IsSideCardLeader(string targetService, ILogger logger)
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
                        logger.LogInformation($"{targetService} is the leader.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error checking if {targetService} is leader. Detailed error: {ex.GetType().Name} - {ex.Message}");
            }

            logger.LogInformation($"{targetService} is not the leader.");
            return false;
        }

        /// <summary>
        /// Tente de promouvoir le SideCard spécifié en tant que leader.
        /// </summary>
        private static async Task<bool> AttemptLeaderPromotion(string targetService, ILogger logger)
        {
            try
            {
                var res = await RestController.Get(
                    new GetRoutingRequest()
                    {
                        TargetService = targetService,
                        Endpoint = $"SideCard/PromoteToLeader",
                        Mode = LoadBalancingMode.Broadcast
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
                logger.LogError(ex, $"Error promoting {targetService} to leader. Detailed error: {ex.GetType().Name} - {ex.Message}");
            }

            logger.LogWarning($"Failed to promote {targetService} to leader.");
            return false;
        }
    }
}