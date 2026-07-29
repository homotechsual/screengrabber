using Microsoft.AspNetCore.Http;
using Screengrabber.Api;

namespace Screengrabber.Api.Tests;

public class ApiKeyAuthTests
{
    [Fact]
    public void ParseConfiguredKeys_HandlesNullEmptyAndTrimming()
    {
        Assert.Empty(ApiKeyAuth.ParseConfiguredKeys(null));
        Assert.Empty(ApiKeyAuth.ParseConfiguredKeys(""));

        var keys = ApiKeyAuth.ParseConfiguredKeys(" key-one, key-two ,, key-three ");

        Assert.Equal(3, keys.Count);
        Assert.Contains("key-one", keys);
        Assert.Contains("key-two", keys);
        Assert.Contains("key-three", keys);
    }

    [Fact]
    public void IsRequestAuthorized_NoConfiguredKeys_AllowsAnonymous()
    {
        var context = new DefaultHttpContext();
        var keys = new HashSet<string>();

        Assert.True(ApiKeyAuth.IsRequestAuthorized(context, keys));
    }

    [Fact]
    public void IsRequestAuthorized_ValidHeader_AllowsRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "abc123";

        var keys = new HashSet<string> { "abc123" };

        Assert.True(ApiKeyAuth.IsRequestAuthorized(context, keys));
    }

    [Fact]
    public void IsRequestAuthorized_InvalidOrMissingHeader_DeniesRequest()
    {
        var context = new DefaultHttpContext();
        var keys = new HashSet<string> { "abc123" };

        Assert.False(ApiKeyAuth.IsRequestAuthorized(context, keys));

        context.Request.Headers["X-Api-Key"] = "wrong";
        Assert.False(ApiKeyAuth.IsRequestAuthorized(context, keys));
    }
}