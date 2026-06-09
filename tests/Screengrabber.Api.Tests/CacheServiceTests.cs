using NSubstitute;
using Screengrabber.Api;
using StackExchange.Redis;
using Xunit;

namespace Screengrabber.Api.Tests;

public class CacheServiceTests
{
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly ICacheService _cache;

    public CacheServiceTests()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.GetDatabase().Returns(_db);
        _cache = new CacheService(multiplexer);
    }

    [Fact]
    public async Task GetAsync_WhenKeyMissing_ReturnsNull()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
           .Returns(RedisValue.Null);

        var result = await _cache.GetAsync("/some/key");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WhenKeyExists_ReturnsBytes()
    {
        var expected = new byte[] { 1, 2, 3 };
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
           .Returns((RedisValue)expected);

        var result = await _cache.GetAsync("/some/key");

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task SetAsync_CallsStringSetWithCorrectTtl()
    {
        var bytes = new byte[] { 4, 5, 6 };
        var ttl   = TimeSpan.FromHours(24);

        await _cache.SetAsync("/some/key", bytes, ttl);

        await _db.Received(1).StringSetAsync(
            "/some/key",
            (RedisValue)bytes,
            ttl,
            keepTtl: false,
            when: When.Always,
            flags: CommandFlags.None);
    }
}
