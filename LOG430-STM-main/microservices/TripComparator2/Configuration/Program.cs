using Application.Interfaces;
using Application.Interfaces.Policies;
using Application.Usecases;
using Configuration.Policies;
using Controllers.Controllers;
using Infrastructure.Clients;
using MassTransit;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.OpenApi.Models;
using MqContracts;
using Newtonsoft.Json;
using RabbitMQ.Client;
using ServiceMeshHelper;
using ServiceMeshHelper.BusinessObjects.InterServiceRequests;
using ServiceMeshHelper.BusinessObjects;
using ServiceMeshHelper.Controllers;





namespace Configuration
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            ConfigureServices(builder.Services);

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

            app.UseAuthorization();

            app.MapControllers();

            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            // D�marrer le ping echo avant de lancer l'application
            var cancellationTokenSource = new CancellationTokenSource();
            
            var mqController = app.Services.GetRequiredService<TripComparatorMqController>();

            await mqController.CallBack(cancellationTokenSource.Token);

            // Lancer l'application
            await app.RunAsync();

            

            // Annuler le ping quand l'application se termine
            cancellationTokenSource.Cancel();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            ConfigureMassTransit(services);

            services.AddControllers().PartManager.ApplicationParts.Add(new AssemblyPart(typeof(CompareTripController).Assembly));

            services.AddEndpointsApiExplorer();

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "TripComparator", Version = "v1" });
                c.EnableAnnotations();
            });

            services.AddSingleton<IHostInfo, HostInfo>();

            services.AddScoped(typeof(IInfiniteRetryPolicy<>), typeof(InfiniteRetryPolicy<>));

            services.AddScoped(typeof(IBackOffRetryPolicy<>), typeof(BackOffRetryPolicy<>));

            services.AddScoped<CompareTimes>();

            services.AddScoped<IRouteTimeProvider, RouteTimeProviderClient>();

            services.AddScoped<IDataStreamWriteModel, MassTransitRabbitMqClient>();

            services.AddScoped<IBusInfoProvider, StmClient>();

            services.AddScoped<TripComparatorMqController>();
        }

        private static void ConfigureMassTransit(IServiceCollection services)
        {
            var hostInfo = new HostInfo();
            
            var routingData = RestController.GetAddress(hostInfo.GetMQServiceName(), LoadBalancingMode.RoundRobin).Result.First();

            var uniqueQueueName = $"time_comparison.node_controller-to-any.query.{Guid.NewGuid()}";

            services.AddMassTransit(x =>
            {
                x.AddConsumer<TripComparatorMqController>();

                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host($"rabbitmq://{ routingData.Host }:{routingData.Port}", c =>
                    {
                        c.RequestedConnectionTimeout(100);
                        c.Heartbeat(TimeSpan.FromMilliseconds(50));
                        c.PublisherConfirmation = true;
                    });

                    cfg.Message<BusPositionUpdated>(topologyConfigurator => topologyConfigurator.SetEntityName("bus_position_updated"));
                    cfg.Message<CoordinateMessage>(topologyConfigurator => topologyConfigurator.SetEntityName("coordinate_message"));

                    cfg.ReceiveEndpoint(uniqueQueueName, endpoint =>
                    {
                        endpoint.ConfigureConsumeTopology = false;

                        endpoint.Bind<CoordinateMessage>(binding =>
                        {
                            binding.ExchangeType = ExchangeType.Topic;
                            binding.RoutingKey = "trip_comparison.query";
                        });

                        endpoint.ConfigureConsumer<TripComparatorMqController>(context);
                    });

                    cfg.Publish<BusPositionUpdated>(p => p.ExchangeType = ExchangeType.Topic);
                });
            });
        }

        private static async Task StartPingingRouteTimeProvider(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
        {

            const int pingIntervalMs = 1000; // Intervalle de ping en millisecondes
            logger.LogInformation("Starting the ping echo to RouteTimeProvider.");


            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Appeler la route 'isAlive' en mode RoundRobin
                    var res = await RestController.Get(
                        new GetRoutingRequest()
                        {
                            TargetService = "RouteTimeProvider",
                            Endpoint = $"RouteTime/isAlive", 
                            Mode = LoadBalancingMode.RoundRobin // Mode RoundRobin pour le ping echo
                        });

                    // It�rer sur les r�sultats asynchrones
                    await foreach (var result in res!.ReadAllAsync())
                    {
                        // V�rifier si le service est en vie
                        if (JsonConvert.DeserializeObject<string>(result.Content) == "isAlive")
                        {
                               // on fait quand c'est Alive
                        }
                        else
                        {
                            
                            logger.LogInformation("RouteTimeProvider is not responding.");
                        }
                        break; // On a v�rifi� la premi�re r�ponse, on peut sortir
                    }
                }
                catch (Exception ex)
                {

                    logger.LogError(ex, $"RouteTimeProvider service is not available. Detailed error: {ex.GetType().Name} - {ex.Message}");
                }

                // Attendre avant de refaire une tentative de ping
                await Task.Delay(pingIntervalMs, cancellationToken);
            }
        }
    }
}