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
        private static bool _isLeader = false;
        public static void Main(string[] args)
        {

            var builder = WebApplication.CreateBuilder(args);

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

            Task.Run(() => CheckingIfLeader(app.Services, logger, cancellationTokenSource.Token));
            //Task.Run(() => Test(logger));
            

            app.UseCors();

            app.MapControllers();

            app.Run();

            cancellationTokenSource.Cancel();




        }

        public static async void Test(ILogger logger)
        {
            var podLeaderID = await ServiceMeshInfoProvider.PodLeaderId;


            // Log the podLeaderID information
            logger.LogInformation($"Pod Leader ID: {podLeaderID}");
        }

        private static async Task CheckingIfLeader(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
        {
            logger.LogInformation("Starting CheckingIfLeader task...");

            const int pingIntervalMs = 50; // Intervalle de ping en millisecondes
            
            var podLeaderID = await ServiceMeshInfoProvider.PodLeaderId;
            
            logger.LogInformation($"Pod Leader ID: {podLeaderID}");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Appeler la route 'isLeader' en mode Brodcast
                    logger.LogInformation("Simulating RestController.Get call for isLeader check...");
                    
                    var res = await RestController.Get(
                        new GetRoutingRequest()
                        {
                            TargetService = podLeaderID,
                            Endpoint = $"Finder/isLeader",
                            Mode = LoadBalancingMode.Broadcast // Utiliser Brodcast pour le ping
                        });

                    // Itérer sur les résultats asynchrones
                    await foreach (var result in res!.ReadAllAsync())
                    {
                        // Vérifier la réponse
                        if (result.Content != null && JsonConvert.DeserializeObject<string>(result.Content) == "isLeader")
                        {
                            logger.LogInformation("Pod leader confirmed as leader.");
                        }
                        else
                        {
                            logger.LogInformation("Pod leader is not the leader.");
                        }
                        break; // On a vérifié la première réponse, on peut sortir
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $" service is not available. Detailed error: {ex.GetType().Name} - {ex.Message}");
                }

                // Attendre avant de refaire une tentative de ping
                await Task.Delay(pingIntervalMs, cancellationToken);
            }
        }
    }
}