using Application.Interfaces.Policies;
using Application.Usecases;
using MassTransit;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MqContracts;
using StackExchange.Redis;
using System.Threading.Tasks;




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
        
        string processingLockKey = "TripComparator:ProcessingLock";
        string statusKey = "TripComparator:ConsumeStatus";
        string startingKey = "TripComparator:StartingCoordinates";
        string destinationKey = "TripComparator:DestinationCoordinates";


        string startingCoordinates = RemoveWhiteSpaces(context.Message.StartingCoordinates);
        string destinationCoordinates = RemoveWhiteSpaces(context.Message.DestinationCoordinates);

        await redisDb.StringSetAsync(startingKey, startingCoordinates);
        await redisDb.StringSetAsync(destinationKey, destinationCoordinates);

        // Définir le statut dans Redis
        await redisDb.StringSetAsync(statusKey, "Called");

        // Lire les coordonnées pour validation
        var storedStartingCoordinates = await redisDb.StringGetAsync(startingKey);
        var storedDestinationCoordinates = await redisDb.StringGetAsync(destinationKey);

        // Acquérir le verrou pour empêcher ProcessInLoop() de s'exécuter en même temps
        var lockAcquired = await redisDb.StringSetAsync(processingLockKey, "Locked", TimeSpan.FromSeconds(30), When.NotExists);
        if (!lockAcquired)
        {
            _logger.LogInformation("Consume() is already running or ProcessInLoop() is active.");
            return;
        }
        try
        {
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

    public async Task ConsumeAlternative(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting ConsumeAlternative to monitor leadership status...");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Vérifiez l'état de la variable d'environnement
                if (IsLeader())
                {
                    _logger.LogInformation("This service is now the Leader. Exiting loop...");

                    // Exécuter le CallBack immédiatement après avoir détecté le rôle de Leader
                    await CallBack();
                    break; // Sortir de la boucle
                }

                _logger.LogDebug("Not a Leader yet. Retrying...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while monitoring leadership status.");
            }

            // Attendre un intervalle avant de vérifier à nouveau
            await Task.Delay(50, cancellationToken); // Ajustez cet intervalle en fonction de vos besoins
        }

        _logger.LogInformation("ConsumeAlternative monitoring stopped.");
    }

    public async Task CallBack()
    {
        _logger.LogInformation("Starting CallBack monitoring...");

        var redisDb = RedisConnectionController.GetDatabase();

        // Clés Redis
        const string statusKey = "TripComparator:ConsumeStatus";
        const string startingKey = "TripComparator:StartingCoordinates";
        const string destinationKey = "TripComparator:DestinationCoordinates";

        try
        {
            // Lire plusieurs valeurs Redis en une seule requête
            var redisValues = await redisDb.StringGetAsync(new RedisKey[] { statusKey, startingKey, destinationKey });

            // Vérification des valeurs Redis
            var consumeStatus = redisValues[0];
            var storedStartingCoordinates = redisValues[1];
            var storedDestinationCoordinates = redisValues[2];

            if (!storedStartingCoordinates.HasValue || !storedDestinationCoordinates.HasValue)
            {
                _logger.LogWarning("Coordinates are missing in Redis. Skipping CallBack execution.");
                return; // Quitter si les coordonnées sont manquantes
            }

            if (consumeStatus != "Called")
            {
                _logger.LogInformation("Not a Leader or status is not 'Called'. Skipping CallBack.");
                return; // Quitter si le statut n'est pas 'Called'
            }

            _logger.LogInformation("This service is the Leader. Executing CallBack logic...");

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
            _logger.LogError(ex, "An error occurred while executing CallBack logic.");
        }

        _logger.LogInformation("CallBack monitoring stopped.");
    }

    private bool IsLeader()
    {
        // Encapsulez la logique pour vérifier le rôle de Leader
        var leaderStatus = Environment.GetEnvironmentVariable("IS_LEADER_TC");
        return leaderStatus == "true";
    }

    private string RemoveWhiteSpaces(string s) => s.Replace(" ", "");

    

}