using StackExchange.Redis;
using System;

namespace ManagerSTM.Redis
{
    public class RedisService
    {
        private readonly IDatabase _db;

        public RedisService(IDatabase db)
        {
            _db = db;
        }

        public void TestConnection()
        {
            _db.StringSet("test_key", "Hello, Redis!");
            var value = _db.StringGet("test_key");
            Console.WriteLine($"Valeur de test récupérée depuis Redis: {value}");
        }

        public IDatabase Database => _db;
    }
}
