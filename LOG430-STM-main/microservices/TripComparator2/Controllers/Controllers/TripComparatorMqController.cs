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
        var isLeader = Environment.GetEnvironmentVariable("IS_LEADER_TC");
        if (isLeader != "true")
        {
            _logger.LogInformation("Not a leader, can't execute Consume().");
            return;
        }

        var redisDb = RedisConnectionController.GetDatabase();
        RedisConnectionController.TestConnection();

        string processingLockKey = "TripComparator:ProcessingLock";
        string statusKey = "TripComparator:ConsumeStatus";
        string startingKey = "TripComparator:StartingCoordinates";
        string destinationKey = "TripComparator:DestinationCoordinates";

        // Acquérir le verrou pour empêcher ProcessInLoop() de s'exécuter en même temps
        var lockAcquired = await redisDb.StringSetAsync(processingLockKey, "Locked", TimeSpan.FromSeconds(30), When.NotExists);
        if (!lockAcquired)
        {
            _logger.LogInformation("Consume() is already running or ProcessInLoop() is active.");
            return;
        }

        try
        {
            // Extraire et sauvegarder les coordonnées
            string startingCoordinates = RemoveWhiteSpaces(context.Message.StartingCoordinates);
            string destinationCoordinates = RemoveWhiteSpaces(context.Message.DestinationCoordinates);

            await redisDb.StringSetAsync(startingKey, startingCoordinates);
            await redisDb.StringSetAsync(destinationKey, destinationCoordinates);

            // Définir le statut dans Redis
            await redisDb.StringSetAsync(statusKey, "Called");

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
        finally
        {
            // Libérer le verrou
            await redisDb.KeyDeleteAsync(processingLockKey);
        }
    }

    public async Task ProcessInLoop(CancellationToken cancellationToken)
    {
        var isLeader = Environment.GetEnvironmentVariable("IS_LEADER_TC");
        if (isLeader != "true")
        {
            _logger.LogInformation("Not a leader, skipping ProcessInLoop.");
            return;
        }

        var redisDb = RedisConnectionController.GetDatabase();
        RedisConnectionController.TestConnection();

        string processingLockKey = "TripComparator:ProcessingLock";
        string statusKey = "TripComparator:ConsumeStatus";
        string startingKey = "TripComparator:StartingCoordinates";
        string destinationKey = "TripComparator:DestinationCoordinates";

        while (!cancellationToken.IsCancellationRequested)
        {
            // Attendre que le verrou soit libéré par Consume()
            var isLocked = await redisDb.StringGetAsync(processingLockKey);
            if (isLocked.HasValue)
            {
                _logger.LogInformation("Processing is locked by another method. Waiting...");
                await Task.Delay(1000, cancellationToken);
                continue;
            }

            try
            {
                // Vérifier si Consume() a été appelé
                var consumeStatus = await redisDb.StringGetAsync(statusKey);
                if (consumeStatus != "Called")
                {
                    _logger.LogInformation("Consume() has not been called yet. Waiting...");
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }

                // Lire les coordonnées
                var storedStartingCoordinates = await redisDb.StringGetAsync(startingKey);
                var storedDestinationCoordinates = await redisDb.StringGetAsync(destinationKey);

                if (!storedStartingCoordinates.HasValue || !storedDestinationCoordinates.HasValue)
                {
                    _logger.LogWarning("Coordinates are missing in Redis. Waiting...");
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }

                _logger.LogInformation("Starting ProcessInLoop...");

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

                _logger.LogInformation("Processing completed successfully.");
                break; // Sortir de la boucle après avoir terminé
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during ProcessInLoop. Retrying...");
                await Task.Delay(1000, cancellationToken);
            }
        }

    }
    private string RemoveWhiteSpaces(string s) => s.Replace(" ", "");
}