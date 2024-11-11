using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Controllers.Controllers
{
    public class RedisConnectionController
    {
        private static Lazy<ConnectionMultiplexer> _lazyConnection;

        static RedisConnectionController()
        {
            _lazyConnection = new Lazy<ConnectionMultiplexer>(() =>
            {
                var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost";
                var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379";
                var configuration = new ConfigurationOptions
                {
                    EndPoints = { $"{redisHost}:{redisPort}" },
                    AbortOnConnectFail = false, // Ne pas échouer si la connexion initiale échoue
                    ConnectRetry = 5,          // Nombre de tentatives de connexion
                    ConnectTimeout = 5000      // Timeout pour chaque tentative
                };

                return ConnectionMultiplexer.Connect(configuration);
            });
        }
        public static ConnectionMultiplexer Connection => _lazyConnection.Value;
        public static IDatabase GetDatabase()
        {
            return Connection.GetDatabase();
        }
    
        public static void TestConnection()
        {
            var db = GetDatabase();
            var pong = db.Ping();
            Console.WriteLine($"Redis Ping Response: {pong.TotalMilliseconds} ms");
        }

        public static async Task WaitForConnectionAsync()
        {
            int retryCount = 0;
            int maxRetries = 1000; // Nombre maximum de tentatives
            TimeSpan delayBetweenRetries = TimeSpan.FromSeconds(1);

            while (retryCount < maxRetries)
            {
                try
                {
                    var db = GetDatabase();
                    var pong = await db.PingAsync(); // Test de la connexion Redis
                    Console.WriteLine($"Redis connected. Ping: {pong.TotalMilliseconds} ms");
                    return; // Sortir si la connexion est établie
                }
                catch (RedisConnectionException)
                {
                    retryCount++;
                    Console.WriteLine($"Retrying Redis connection... Attempt {retryCount}/{maxRetries}");
                    await Task.Delay(delayBetweenRetries);
                }
            }

            throw new Exception("Unable to connect to Redis after multiple attempts.");
        }

    }
}
