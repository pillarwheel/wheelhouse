using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WheelHouse.Infrastructure.Agents;
using Xunit;

namespace WheelHouse.Tests;

public class GeminiContextCacheRegistryTests
{
    [Fact]
    public void KeyFor_Is_Stable_And_Content_Sensitive()
    {
        Assert.Equal(GeminiContextCache.KeyFor("abc"), GeminiContextCache.KeyFor("abc"));
        Assert.NotEqual(GeminiContextCache.KeyFor("abc"), GeminiContextCache.KeyFor("abd"));
    }

    [Fact]
    public void Entries_Expire_And_Can_Be_Invalidated()
    {
        var cache = new GeminiContextCache();
        var now = DateTimeOffset.UtcNow;
        cache.Store("k", "cachedContents/x", now.AddMinutes(5));

        Assert.True(cache.TryGet("k", now, out var name));
        Assert.Equal("cachedContents/x", name);

        Assert.False(cache.TryGet("k", now.AddMinutes(6), out _)); // expired
        cache.Store("k", "cachedContents/y", now.AddMinutes(5));
        cache.Invalidate("k");
        Assert.False(cache.TryGet("k", now, out _));
    }
}

/// <summary>
/// Drives GeminiService against a scripted HTTP handler to prove the explicit context-caching
/// flow: large contexts are uploaded once as cachedContents and referenced afterwards; small
/// contexts stay inline; a rejected cache falls back to inline transparently.
/// </summary>
public class GeminiContextCachingTests
{
    private const string PlanJson =
        """{"candidates":[{"content":{"parts":[{"text":"PLAN"}]}}]}""";
    private const string CacheCreatedJson =
        """{"name":"cachedContents/test-cache-1"}""";

    private sealed record Call(string Url, string Body);

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<Call> Calls { get; } = new();
        public Func<Call, HttpResponseMessage> Respond { get; set; } = _ => Json(PlanJson);

        public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = new Call(request.RequestUri!.ToString(),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            Calls.Add(call);
            return Respond(call);
        }
    }

    private static (GeminiService Service, ScriptedHandler Handler) NewService(int minChars = 100)
    {
        var handler = new ScriptedHandler
        {
            Respond = call => call.Url.Contains("cachedContents")
                ? ScriptedHandler.Json(CacheCreatedJson)
                : ScriptedHandler.Json(PlanJson)
        };
        var options = new GeminiOptions
        {
            ApiKey = "fake-key",
            BaseUrl = "http://unit.test/v1beta",
            ContextCacheMinChars = minChars,
            ContextCacheTtlSeconds = 600
        };
        var service = new GeminiService(
            new HttpClient(handler), options, new GeminiContextCache(), NullLogger<GeminiService>.Instance);
        return (service, handler);
    }

    private static string LargeContext => string.Concat(
        Enumerable.Repeat("public class Filler { /* repository context */ }\n", 20));

    [Fact]
    public async Task Large_Context_Is_Cached_Once_And_Referenced_Afterwards()
    {
        var (service, handler) = NewService();

        var plan = await service.GenerateResearchPlanAsync("add feature", LargeContext);
        Assert.Equal("PLAN", plan);

        // Call 1 creates the cache; call 2 generates referencing it, without the inline context.
        Assert.Equal(2, handler.Calls.Count);
        Assert.Contains("cachedContents", handler.Calls[0].Url);
        Assert.Contains("Filler", handler.Calls[0].Body);
        Assert.Contains("generateContent", handler.Calls[1].Url);
        Assert.Contains("cachedContents/test-cache-1", handler.Calls[1].Body);
        Assert.DoesNotContain("Filler", handler.Calls[1].Body);

        // Second plan with the same context reuses the registry entry: no new cachedContents POST.
        await service.GenerateResearchPlanAsync("another goal", LargeContext);
        Assert.Equal(3, handler.Calls.Count);
        Assert.Contains("generateContent", handler.Calls[2].Url);
        Assert.Contains("cachedContents/test-cache-1", handler.Calls[2].Body);
    }

    [Fact]
    public async Task Small_Context_Stays_Inline()
    {
        var (service, handler) = NewService(minChars: 100_000);

        var plan = await service.GenerateResearchPlanAsync("add feature", "tiny context");

        Assert.Equal("PLAN", plan);
        var call = Assert.Single(handler.Calls);
        Assert.Contains("generateContent", call.Url);
        Assert.Contains("tiny context", call.Body);
        Assert.DoesNotContain("cachedContent\"", call.Body);
    }

    [Fact]
    public async Task Rejected_Cache_Falls_Back_To_Inline_Context()
    {
        var (service, handler) = NewService();
        handler.Respond = call =>
            call.Url.Contains("cachedContents") ? ScriptedHandler.Json(CacheCreatedJson)
            : call.Body.Contains("cachedContents/test-cache-1")
                ? ScriptedHandler.Json("""{"error":"cache expired"}""", HttpStatusCode.BadRequest)
                : ScriptedHandler.Json(PlanJson);

        var plan = await service.GenerateResearchPlanAsync("add feature", LargeContext);

        Assert.Equal("PLAN", plan);
        // create cache → cached generate (400) → inline generate (200)
        Assert.Equal(3, handler.Calls.Count);
        Assert.Contains("Filler", handler.Calls[2].Body); // inline retry carries the context
        Assert.DoesNotContain("cachedContents/test-cache-1", handler.Calls[2].Body);
    }

    [Fact]
    public async Task Cache_Mode_Off_Disables_Caching_Entirely()
    {
        var handler = new ScriptedHandler();
        var options = new GeminiOptions
        {
            ApiKey = "fake-key",
            BaseUrl = "http://unit.test/v1beta",
            ContextCacheMode = "off",
            ContextCacheMinChars = 1
        };
        var service = new GeminiService(
            new HttpClient(handler), options, new GeminiContextCache(), NullLogger<GeminiService>.Instance);

        await service.GenerateResearchPlanAsync("goal", LargeContext);

        var call = Assert.Single(handler.Calls);
        Assert.Contains("generateContent", call.Url);
        Assert.Contains("Filler", call.Body);
    }
}
