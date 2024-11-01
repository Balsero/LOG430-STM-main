using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Application.Interfaces;
using Application.Usecases;
using Microsoft.AspNetCore.RateLimiting;
using RouteTimeProvider.RestClients;
using ServiceMeshHelper;
using ServiceMeshHelper.BusinessObjects.InterServiceRequests;
using ServiceMeshHelper.BusinessObjects;
using ServiceMeshHelper.Controllers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

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

            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            
            var cancellationTokenSource = new CancellationTokenSource();

            Task.Run(() => CheckingIfLeader(app.Services, logger, cancellationTokenSource.Token));
            cancellationTokenSource.Cancel();

            app.UseSwagger();
            app.UseSwaggerUI();

            app.UseCors(
                options =>
                {
                    options.AllowAnyOrigin();
                    options.AllowAnyHeader();
                    options.AllowAnyMethod();
                }
            );

            app.UseCors();

            app.MapControllers();

            app.Run();
            



        }

        public static async void Test(ILogger logger)
        {   
            var podLeaderID = await ServiceMeshInfoProvider.PodLeaderId;

            
            // Log the podLeaderID information
            logger.LogInformation($"Pod Leader ID: {podLeaderID}");
        }

        private static async Task CheckingIfLeader(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
        {

            const int pingIntervalMs = 1000; // Intervalle de ping en millisecondes
            var podLeaderID = await ServiceMeshInfoProvider.PodLeaderId;


            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Appeler la route 'isAlive' en mode RoundRobin
                    var res = await RestController.Get(
                        new GetRoutingRequest()
                        {
                            TargetService = podLeaderID,
                            Endpoint = $"Finder/isAlive",
                            Mode = LoadBalancingMode.RoundRobin // Mode RoundRobin pour le ping echo
                        });

                    // Itérer sur les résultats asynchrones
                    await foreach (var result in res!.ReadAllAsync())
                    {
                        // Vérifier si le service est en vie
                        if (JsonConvert.DeserializeObject<string>(result.Content) == "isAlive")
                        {
                            logger.LogInformation($"Pod Leader ID: {podLeaderID}");
                            logger.LogInformation("STM is responding.");
                        }
                        else
                        {

                            logger.LogInformation("STM is not responding.");
                        }
                        break; // On a vérifié la première réponse, on peut sortir
                    }
                }
                catch (Exception ex)
                {

                    logger.LogError(ex, $"STM service is not available. Detailed error: {ex.GetType().Name} - {ex.Message}");
                }

                // Attendre avant de refaire une tentative de ping
                await Task.Delay(pingIntervalMs, cancellationToken);
            }
        }
    }
}