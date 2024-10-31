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
            

            builder.Services.AddRateLimiter(_ => _
                .AddFixedWindowLimiter(policyName: "fixed", options =>
                {
                    options.PermitLimit = 2;
                    options.Window = TimeSpan.FromSeconds(10);
                    options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    options.QueueLimit = 0;
                }));

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

            Task.Run(() => StartPingingSideCard(app.Services, logger, cancellationTokenSource.Token));

            app.UseCors();

            app.MapControllers();

            app.Run();

            // Annuler le ping quand l'application se termine
            cancellationTokenSource.Cancel();
        }
        private static async Task StartPingingSideCard(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
        {
            const int pingIntervalMs = 1000; // Intervalle de ping en millisecondes
            logger.LogInformation("Starting the ping echo to RouteTimeProvider in broadcast mode.");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Appeler la route 'isAlive' en mode Broadcast
                    var res = await RestController.Get(
                        new GetRoutingRequest()
                        {
                            TargetService = "STM.SideCard",
                            Endpoint = $"SideCard/isAlive",
                            Mode = LoadBalancingMode.Broadcast // Mode Broadcast pour ping echo
                        });

                    // Itérer sur les résultats asynchrones
                    bool anyServiceAlive = false;
                    await foreach (var result in res!.ReadAllAsync())
                    {
                        // Vérifier si le service est en vie
                        if (JsonConvert.DeserializeObject<string>(result.Content) == "isAlive")
                        {
                            anyServiceAlive = true;
                            break; // Au moins un service est en vie, on peut sortir
                        }
                    }

                    // Loguer le résultat final pour tous les services
                    if (anyServiceAlive)
                    {
                        logger.LogInformation("At least one SideCard instance is responding.");
                    }
                    else
                    {
                        logger.LogInformation("No SideCard instance is responding.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"SideCard service is not available. Detailed error: {ex.GetType().Name} - {ex.Message}");
                }

                // Attendre avant de refaire une tentative de ping
                await Task.Delay(pingIntervalMs, cancellationToken);
            }
        }
    }
}