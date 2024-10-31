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

namespace RouteTimeProvider
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Validate();

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

            builder.Services.AddSingleton<IRouteTimeProvider, TomTomClient>();
            builder.Services.AddScoped<CarTravel>();

            builder.Services.AddControllers();

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            var logger = app.Services.GetRequiredService<ILogger<Program>>();

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
            
            Test(logger);
             
            app.Run();

            
            
        }

        public static async void Test(ILogger logger)
        {   
            var podLeaderID = await ServiceMeshInfoProvider.PodLeaderId;

            
            // Log the podLeaderID information
            logger.LogInformation($"Pod Leader ID: {podLeaderID}");

        }
        



        private static void Validate()
        {
            var apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? throw new Exception("API_KEY environment variable not found");

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException("API_KEY",
                    "The api key was not defined in the env variables, this is critical");
        }
    }
}