﻿using System.Threading.Channels;
using Application.BusinessObjects;
using Application.DTO;
using Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.Usecases
{
    public class CompareTimes
    {
        private readonly IRouteTimeProvider _routeTimeProvider;

        private readonly IBusInfoProvider _iBusInfoProvider;

        private readonly IDataStreamWriteModel _dataStreamWriteModel;

        //This is a very aggressive polling rate, is there a better way to do this?
        private readonly PeriodicTimer _periodicTimer = new(TimeSpan.FromMilliseconds(50));

        private int _averageCarTravelTime;

        private RideDto? _optimalBus;

        private readonly ILogger<CompareTimes> _logger;

        public CompareTimes(IRouteTimeProvider routeTimeProvider, IBusInfoProvider iBusInfoProvider, IDataStreamWriteModel dataStreamWriteModel, ILogger<CompareTimes> logger)
        {
            _routeTimeProvider = routeTimeProvider;
            _iBusInfoProvider = iBusInfoProvider;
            _dataStreamWriteModel = dataStreamWriteModel;
            _logger = logger;
        }

        public async Task<Channel<IBusPositionUpdated>> BeginComparingBusAndCarTime(string startingCoordinates, string destinationCoordinates)
        {
            var redisDb = RedisConnectionManager.GetDatabase();
            const string travelTimeKey = "TripComparator:CurrentTime";
            // Fonction locale pour rafraîchir l'état
            async Task<string> RefreshStateAsync()
            {
                var state = await redisDb.StringGetAsync("TripComparator:CurrentState");
                _logger.LogInformation($"Refreshed CurrentState: {state}");
                return state.HasValue ? state.ToString() : string.Empty; // Retourner une chaîne vide si null
            }

            // Récupérer l'état initial
            string currentState = await RefreshStateAsync();
            

            _logger.LogInformation($"Initial CurrentState: {currentState}");

            // Étape 1 : GetTravelTimeInSeconds
            if (string.IsNullOrEmpty(currentState))
            {
                _logger.LogInformation("Executing GetTravelTimeInSeconds...");
                _averageCarTravelTime = await _routeTimeProvider.GetTravelTimeInSeconds(startingCoordinates, destinationCoordinates);

                await redisDb.StringSetAsync(travelTimeKey, _averageCarTravelTime.ToString());




                if (_averageCarTravelTime < 1)
                {
                    throw new Exception("Car travel time data was invalid.");
                }

                await redisDb.StringSetAsync("TripComparator:CurrentState", "GetTravelTimeInSeconds");
                currentState = "GetTravelTimeInSeconds"; // Mettre à jour localement
                _logger.LogInformation("State updated to GetTravelTimeInSeconds");
            }

            // Étape 2 : GetBestBus
            if (currentState == "GetTravelTimeInSeconds")
            {
                _logger.LogInformation("Executing GetBestBus...");
                _optimalBus = await _iBusInfoProvider.GetBestBus(startingCoordinates, destinationCoordinates);

                if (_optimalBus == null)
                {
                    throw new Exception("No optimal bus found.");
                }

                await redisDb.StringSetAsync("TripComparator:CurrentState", "GetBestBus");
                currentState = "GetBestBus";
                _logger.LogInformation("State updated to GetBestBus");
            }

            // Étape 3 : BeginTracking
            if (currentState == "GetBestBus")
            {
                _logger.LogInformation("Executing BeginTracking...");
                if (_optimalBus == null)
                {
                    throw new Exception("Bus data was null. Cannot begin tracking.");
                }

                await _iBusInfoProvider.BeginTracking(_optimalBus);

                await redisDb.StringSetAsync("TripComparator:CurrentState", "BeginTracking");
                currentState = "BeginTracking";
                _logger.LogInformation("State updated to BeginTracking");
            }

            // Créer le canal pour les mises à jour
            var channel = Channel.CreateUnbounded<IBusPositionUpdated>();
            return channel;
        }

        //Is polling ideal?
        public async Task PollTrackingUpdate(ChannelWriter<IBusPositionUpdated> channel)
        {
            var redisDb = RedisConnectionManager.GetDatabase();

            // Fonction locale pour rafraîchir l'état
            async Task<string> RefreshStateAsync()
            {
                var state = await redisDb.StringGetAsync("TripComparator:CurrentState");
                _logger.LogInformation($"Refreshed CurrentState: {state}");
                return state.HasValue ? state.ToString() : string.Empty; // Retourner une chaîne vide si null
            }

            // Récupérer l'état initial
            string currentState = await RefreshStateAsync();

            if (currentState == "BeginTracking" || currentState == "TrackingComplete")

            {

                //if (_optimalBus is null) throw new Exception("bus data was null");

                var trackingOnGoing = true;

                while (trackingOnGoing && await _periodicTimer.WaitForNextTickAsync())
                {
                    var trackingResult = await _iBusInfoProvider.GetTrackingUpdate();

                    if (trackingResult is null) continue;

                    trackingOnGoing = !trackingResult.TrackingCompleted;

                    var busPosition = new BusPosition()
                    {
                        Message = trackingResult.Message + $"\nCar: { await redisDb.StringGetAsync("TripComparator:CurrentTime")} seconds",
                        Seconds = trackingResult.Duration,
                    };

                    await channel.WriteAsync(busPosition);
                }
                channel.Complete();
            }
        }

        public async Task WriteToStream(ChannelReader<IBusPositionUpdated> channelReader)
        {
            await foreach (var busPositionUpdated in channelReader!.ReadAllAsync())
            {
                await _dataStreamWriteModel.Produce(busPositionUpdated);
            }
        }
    }
}
