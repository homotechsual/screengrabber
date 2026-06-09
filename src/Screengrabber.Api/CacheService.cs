using StackExchange.Redis;

namespace Screengrabber.Api;

public interface ICacheService
{
    Task<byte[]?> GetAsync(string key);
    Task SetAsync(string key, byte[] value, TimeSpan ttl);
}

public sealed class CacheService(IConnectionMultiplexer redis) : ICacheService
{
    private readonly IDatabase _db = redis.GetDatabase();

    public async Task<byte[]?> GetAsync(string key)
    {
        var value = await _db.StringGetAsync(key);
        return (byte[]?)value;
    }

    public Task SetAsync(string key, byte[] value, TimeSpan ttl)
        => _db.StringSetAsync(key, value, ttl);
}
