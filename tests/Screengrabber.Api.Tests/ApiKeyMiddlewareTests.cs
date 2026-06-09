using Screengrabber.Api;
using Xunit;

namespace Screengrabber.Api.Tests;

public class ApiKeyMiddlewareTests
{
    [Fact]
    public void IsAuthorized_NoConfiguredKeys_AlwaysTrue()
    {
        var empty = new HashSet<string>();
        Assert.True(ApiKeyMiddleware.IsAuthorized(null,      empty));
        Assert.True(ApiKeyMiddleware.IsAuthorized("",        empty));
        Assert.True(ApiKeyMiddleware.IsAuthorized("anykey",  empty));
    }

    [Fact]
    public void IsAuthorized_ValidKey_ReturnsTrue()
    {
        var keys = new HashSet<string> { "key-one", "key-two" };
        Assert.True(ApiKeyMiddleware.IsAuthorized("key-one", keys));
        Assert.True(ApiKeyMiddleware.IsAuthorized("key-two", keys));
    }

    [Fact]
    public void IsAuthorized_InvalidKey_ReturnsFalse()
    {
        var keys = new HashSet<string> { "key-one" };
        Assert.False(ApiKeyMiddleware.IsAuthorized("wrong", keys));
        Assert.False(ApiKeyMiddleware.IsAuthorized(null,    keys));
        Assert.False(ApiKeyMiddleware.IsAuthorized("",      keys));
    }

    [Fact]
    public void IsAuthorized_KeysAreCaseSensitive()
    {
        var keys = new HashSet<string> { "SecretKey" };
        Assert.False(ApiKeyMiddleware.IsAuthorized("secretkey", keys));
        Assert.False(ApiKeyMiddleware.IsAuthorized("SECRETKEY", keys));
        Assert.True(ApiKeyMiddleware.IsAuthorized("SecretKey",  keys));
    }
}
