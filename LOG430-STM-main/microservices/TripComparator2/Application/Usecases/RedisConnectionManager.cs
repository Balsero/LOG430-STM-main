using StackExchange.Redis;

namespace Application
{
    public static class RedisConnectionManager
    {
        private static readonly Lazy<ConnectionMultiplexer> LazyConnection = new Lazy<ConnectionMultiplexer>(() =>
        {
            var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost";
            var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379";
            var configuration = new ConfigurationOptions
            {
                EndPoints = { $"{redisHost}:{redisPort}" },
                AbortOnConnectFail = false,
                ConnectRetry = 5,
                ConnectTimeout = 5000
            };
            return ConnectionMultiplexer.Connect(configuration);
        });

        public static ConnectionMultiplexer Connection => LazyConnection.Value;

        public static IDatabase GetDatabase()
        {
            return Connection.GetDatabase();
        }

        public static async Task<bool> TestConnectionAsync()
        {
            try
            {
                var db = GetDatabase();
                var pong = await db.PingAsync();
                Console.WriteLine($"Redis Ping Response: {pong.TotalMilliseconds} ms");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Redis Connection Failed: {ex.Message}");
                return false;
            }
        }
    }
}
