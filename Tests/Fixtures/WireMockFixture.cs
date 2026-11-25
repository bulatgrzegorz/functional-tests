using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using WireMock.Admin.Mappings;
using WireMock.Net.Testcontainers;

namespace Tests.Fixtures;

public class WireMockFixture : FixtureBase
{
    private WireMockContainer _container = null!;
    public string Url => _container.GetPublicUrl().TrimEnd('/');
    // Containers on Aspire's network reach the host-published port through the Docker host alias.
    public string UrlForContainers => new UriBuilder(Url) { Host = "host.docker.internal" }.Uri.ToString().TrimEnd('/');

    protected override async Task Initialize()
    {
        _container = new WireMockContainerBuilder().Build();
        await _container.StartAsync();
    }
    
    public override async ValueTask DisposeAsync()
    {
        try
        {
            await _container.DisposeAsync();
        }
        catch
        {
            // ignored
        }
    }

    public async Task DefineMock(MockedRequest request, MockedResponse response)
    {
        var adminClient = _container.CreateWireMockAdminClient();

        var mappingModel = new MappingModelBuilder()
            .WithRequest(r =>
                r.WithAddress(request.Path)
                    .WithMethod(request.Method)
                    .WithBody(request.Body, request.BodyMatcher)
                    .WithHeaders(request.Headers)
                    .WithConfiguration(request.Configure)
                    .Build())
            .WithResponse(r =>
                r.WithBody(response.Body)
                    .WithStatusCode(response.StatusCode)
                    .WithHeaders(response.Headers.ToDictionary(x => x.Key, x => (object)x.Value))
                    .WithConfiguration(response.Configure)
                    .Build())
            .Build();
        
        var mappingResponse = await adminClient.PostMappingAsync(mappingModel);
        if(!string.IsNullOrWhiteSpace(mappingResponse.Error)) throw new Exception(mappingResponse.Error);
    }
    
    public class MockedRequest
    {
        public required Method Method { get; init; } = Method.Get;
        public string Path { get; init; } = string.Empty;
        public object? Body { get; init; }
        public BodyMatcher BodyMatcher { get; init; } = BodyMatcher.Exact;
        public List<(string Key, string Value)> Headers { get; init; } = [];
        public Func<RequestModelBuilder, RequestModelBuilder>? Configure { get; init; } = null;
    }

    public class MockedResponse
    {
        public required int StatusCode { get; init; }
        public List<(string Key, string Value)> Headers { get; init; } = [];
        public object? Body { get; init; }
        public Func<ResponseModelBuilder, ResponseModelBuilder>? Configure { get; init; } = null;
    }
    
    public enum Method{Get, Post, Delete, Put, Patch, Head, Options, Trace}
    public enum BodyMatcher{Exact, JsonPartialWildcard, Regex}


}

public static class MockBuilderExtensions
{
    extension(ResponseModelBuilder responseModelBuilder)
    {
        public ResponseModelBuilder WithConfiguration(Func<ResponseModelBuilder, ResponseModelBuilder>? configure)
        {
            return configure is null ? responseModelBuilder : configure.Invoke(responseModelBuilder);
        }
        
        public ResponseModelBuilder WithBody(object? body)
        {
            if(body is null) return responseModelBuilder;

            var bodyPattern = body switch
            {
                string value => value,
                _ => JsonSerializer.Serialize(body)
            };
            
            responseModelBuilder = responseModelBuilder.WithBody(bodyPattern);
            
            return responseModelBuilder;
        }
    }

    extension(RequestModelBuilder requestModelBuilder)
    {
        public RequestModelBuilder WithConfiguration(Func<RequestModelBuilder, RequestModelBuilder>? configure)
        {
            return configure is null ? requestModelBuilder : configure.Invoke(requestModelBuilder);
        }

        public RequestModelBuilder WithMethod(WireMockFixture.Method method)
        {
            return method switch
            {
                WireMockFixture.Method.Get => requestModelBuilder.UsingGet(),
                WireMockFixture.Method.Post => requestModelBuilder.UsingPost(),
                WireMockFixture.Method.Delete => requestModelBuilder.UsingDelete(),
                WireMockFixture.Method.Put => requestModelBuilder.UsingPut(),
                WireMockFixture.Method.Patch => requestModelBuilder.UsingPatch(),
                WireMockFixture.Method.Head => requestModelBuilder.UsingHead(),
                WireMockFixture.Method.Options => requestModelBuilder.UsingOptions(),
                WireMockFixture.Method.Trace => requestModelBuilder.UsingTrace(),
                _ => throw new ArgumentOutOfRangeException(nameof(method), method, null)
            };
        }

        public RequestModelBuilder WithAddress(string url)
        {
            ArgumentNullException.ThrowIfNull(url);
            
            var pathParts = url.Split('?', 2,  StringSplitOptions.RemoveEmptyEntries);
            
            requestModelBuilder = requestModelBuilder.WithPath(pathParts[0]);
            if(pathParts.Length <= 1 || string.IsNullOrWhiteSpace(pathParts[1])) return requestModelBuilder;
            
            var @params = QueryHelpers.ParseQuery(pathParts[1]).Select(x =>
            {
                return new ParamModel()
                {
                    IgnoreCase = true,
                    Name = x.Key,
                    Matchers = x.Value.Select(v => new MatcherModel()
                    {
                        IgnoreCase = true,
                        Name = "WildcardMatcher",
                        Pattern = v
                    }).ToArray()
                };
            });
            
            requestModelBuilder = requestModelBuilder.WithParams(@params.ToList);
            
            return requestModelBuilder;
        }

        public RequestModelBuilder WithHeaders(List<(string Key, string Value)> headers)
        {
            List<HeaderModel> headerModels = [];

            if (Activity.Current is not null)
            {
                var traceId = Activity.Current.TraceId.ToString();
                headerModels.Add(new HeaderModel()
                {
                    Name = "traceparent",
                    IgnoreCase = true,
                    Matchers = [
                    new MatcherModel()
                    {
                        IgnoreCase = true,
                        Name = "WildcardMatcher",
                        Pattern = $"*{traceId}*"
                    }]
                });
            }

            if (headers is { Count: > 0 })
            {
                headerModels.AddRange(headers.Select(x => new HeaderModel()
                {
                    Name = x.Key,
                    IgnoreCase = true,
                    Matchers = [
                        new MatcherModel()
                        {
                            IgnoreCase = true,
                            Name = "WildcardMatcher",
                            Pattern = x.Value
                        }
                    ]
                }));
            }
            
            if(headerModels.Count == 0) return requestModelBuilder;
            
            requestModelBuilder = requestModelBuilder.WithHeaders(headerModels);
            
            return requestModelBuilder;
        }

        public RequestModelBuilder WithBody(object? body, WireMockFixture.BodyMatcher bodyMatcher)
        {
            if(body is null) return requestModelBuilder;

            var matcherName = bodyMatcher switch
            {
                WireMockFixture.BodyMatcher.Exact => "ExactMatcher",
                WireMockFixture.BodyMatcher.JsonPartialWildcard => "JsonPartialWildcardMatcher",
                WireMockFixture.BodyMatcher.Regex => "RegexMatcher",
                _ => throw new ArgumentOutOfRangeException(nameof(bodyMatcher), bodyMatcher, null)
            };

            var bodyPattern = body switch
            {
                string value => value,
                _ => JsonSerializer.Serialize(body)
            };

            requestModelBuilder = requestModelBuilder.WithBody(x =>
            {
                x.WithMatcher(mb => mb
                    .WithName(matcherName)
                    .WithPattern(bodyPattern)
                    .WithIgnoreCase(true));
            });

            return requestModelBuilder;
        }
    }
}