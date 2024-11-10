using Application.Interfaces.Policies;
using Application.Usecases;
using MassTransit;
using Microsoft.Extensions.Logging;
using MqContracts;
using StackExchange.Redis;




namespace Controllers.Controllers;

public class TripComparatorMqController : IConsumer<CoordinateMessage>
{
    private readonly CompareTimes _compareTimes;
    private readonly IInfiniteRetryPolicy<TripComparatorMqController> _infiniteRetryPolicy;
    private readonly IBackOffRetryPolicy<TripComparatorMqController> _backOffRetryPolicy;

    private readonly ILogger<TripComparatorMqController> _logger;

    public TripComparatorMqController(
        ILogger<TripComparatorMqController> logger,
        CompareTimes compareTimes,
        IInfiniteRetryPolicy<TripComparatorMqController> infiniteRetryPolicy,
        IBackOffRetryPolicy<TripComparatorMqController> backOffRetryPolicy)
    {
        _logger = logger;
        _compareTimes = compareTimes;
        _infiniteRetryPolicy = infiniteRetryPolicy;
        _backOffRetryPolicy = backOffRetryPolicy;
    }

    public async Task Consume(ConsumeContext<CoordinateMessage> context)
    {
        
            string startingCoordinates = context.Message.StartingCoordinates;
            string destinationCoordinates = context.Message.DestinationCoordinates;

          _logger.LogInformation($"Comparing trip duration from {startingCoordinates} to {destinationCoordinates}");

        startingCoordinates = RemoveWhiteSpaces(startingCoordinates);
            destinationCoordinates = RemoveWhiteSpaces(destinationCoordinates);

        try
        {

            // Sauvegarder les coordonnées dans Redis
            string startingKey = "TripComparator:StartingCoordinates";
            string destinationKey = "TripComparatorDestinationCoordinates";

            var redisDb = RedisConnectionController.GetDatabase();
            RedisConnectionController.TestConnection();

            await redisDb.StringSetAsync(startingKey, startingCoordinates);
            await redisDb.StringSetAsync(destinationKey, destinationCoordinates);

            // Lire les coordonnées pour validation
            var storedStartingCoordinates = await redisDb.StringGetAsync(startingKey);
            var storedDestinationCoordinates = await redisDb.StringGetAsync(destinationKey);

            if (!storedStartingCoordinates.HasValue || !storedDestinationCoordinates.HasValue)
            {
                throw new Exception("Failed to save or retrieve coordinates from Redis.");
            }

            // Exécuter les fonctions principales
            var producer = await _compareTimes.BeginComparingBusAndCarTime(
                storedStartingCoordinates.ToString(),
                storedDestinationCoordinates.ToString()
            );

            _ = _infiniteRetryPolicy.ExecuteAsync(async () =>
                await _compareTimes.PollTrackingUpdate(producer.Writer)
            );

            _ = _backOffRetryPolicy.ExecuteAsync(async () =>
                await _compareTimes.WriteToStream(producer.Reader)
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while consuming CoordinateMessage.");
            throw;
        }
    }
    private string RemoveWhiteSpaces(string s) => s.Replace(" ", "");
}