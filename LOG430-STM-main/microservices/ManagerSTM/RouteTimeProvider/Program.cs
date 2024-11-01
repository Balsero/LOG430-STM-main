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

            Task.Run(() => StartPingingSideCards(app.Services, logger, cancellationTokenSource.Token));

            app.UseCors();

            app.MapControllers();

            app.Run();

            // Annuler le ping quand l'application se termine
            cancellationTokenSource.Cancel();
        }
        private static async Task StartPingingSideCards(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
        {
            const int pingIntervalMs = 1000; // Intervalle de ping en millisecondes
       

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                  
                    bool sideCard1Alive = await PingService("STM.SideCard", logger);
                    bool sideCard2Alive = await PingService("STM2.SideCard2", logger);

                    // Loguer le résultat final pour SideCard et SideCard2
                    if (sideCard1Alive && sideCard2Alive)
                    {
                        logger.LogInformation("Both SideCard and SideCard2 instances are responding.");
                    }
                    else if (sideCard1Alive)
                    {
                        logger.LogInformation("Only SideCard instance is responding.");
                    }
                    else if (sideCard2Alive)
                    {
                        logger.LogInformation("Only SideCard2 instance is responding.");
                    }
                    else
                    {
                        logger.LogInformation("No SideCard instance is responding.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"An error occurred while checking SideCard services. Detailed error: {ex.GetType().Name} - {ex.Message}");
                }

                // Attendre avant de refaire une tentative de ping
                await Task.Delay(pingIntervalMs, cancellationToken);
            }
        }

        private static async Task<bool> PingService(string targetService, ILogger logger)
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
                logger.LogError(ex, $"Failed to reach {targetService}. Detailed error: {ex.GetType().Name} - {ex.Message}");
            }

            return false;
        }
    }
}