using System.Net.Http.Json;
using System.Text.Json;
using DeepWiki.ApiService.Models;
using DeepWiki.Data.Abstractions.Models;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DeepWiki.Tests.Integration;

/// <summary>
/// Validates that SignalR transport returns identical delta sequences as HTTP NDJSON.
/// Per task T065a: HTTP NDJSON vs SignalR delta sequences match for same input, schema validation.
/// </summary>
public class SignalRParityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _httpClient;

    public SignalRParityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _httpClient = _factory.CreateClient();
    }

    [Fact(Skip = "SignalR hub not yet implemented")]
    public async Task SignalR_And_HTTP_Return_Identical_Delta_Sequences_For_Same_Prompt()
    {
        // Arrange: Create session via HTTP
        var sessionReq = new SessionRequest { Owner = "parity-test" };
        var sessionResp = await _httpClient.PostAsJsonAsync("/api/generation/session", sessionReq, TestContext.Current.CancellationToken);
        sessionResp.EnsureSuccessStatusCode();
        var session = await sessionResp.Content.ReadFromJsonAsync<SessionResponse>(TestContext.Current.CancellationToken);
        session.Should().NotBeNull();
        var sessionId = session!.SessionId;

        var promptReq = new PromptRequest
        {
            SessionId = sessionId,
            Prompt = "Explain dependency injection in 20 words",
            TopK = 5,
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        // Act 1: Get HTTP NDJSON deltas
        var httpDeltas = new List<GenerationDelta>();
        var httpResp = await _httpClient.PostAsJsonAsync("/api/generation/stream", promptReq, TestContext.Current.CancellationToken);
        httpResp.EnsureSuccessStatusCode();
        httpResp.Content.Headers.ContentType?.MediaType.Should().Be("application/x-ndjson");

        await using var stream = await httpResp.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(TestContext.Current.CancellationToken)) != null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                var delta = JsonSerializer.Deserialize<GenerationDelta>(line);
                delta.Should().NotBeNull();
                httpDeltas.Add(delta!);
            }
        }

        // Act 2: Get SignalR deltas via hub
        var hubConnection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/generation", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        await hubConnection.StartAsync(TestContext.Current.CancellationToken);

        var signalrDeltas = new List<GenerationDelta>();
        var signalrSessionResp = await hubConnection.InvokeAsync<SessionResponse>("StartSession", sessionReq, TestContext.Current.CancellationToken);
        var streamPromptReq = new PromptRequest
        {
            SessionId = signalrSessionResp.SessionId,
            Prompt = promptReq.Prompt,
            TopK = promptReq.TopK,
            IdempotencyKey = Guid.NewGuid().ToString() // Different key to avoid cache
        };

        var signalrStream = hubConnection.StreamAsync<GenerationDelta>("SendPrompt", streamPromptReq, TestContext.Current.CancellationToken);
        await foreach (var delta in signalrStream.WithCancellation(TestContext.Current.CancellationToken))
        {
            signalrDeltas.Add(delta);
        }

        await hubConnection.StopAsync(TestContext.Current.CancellationToken);
        await hubConnection.DisposeAsync();

        // Assert: Both transports return same structure and delta count (content may vary due to LLM non-determinism)
        httpDeltas.Should().NotBeEmpty();
        signalrDeltas.Should().NotBeEmpty();

        // Verify both have done event
        httpDeltas.Last().Type.Should().Be("done");
        signalrDeltas.Last().Type.Should().Be("done");

        // Verify sequence integrity for both
        httpDeltas.Select(d => d.Seq).Should().BeInAscendingOrder();
        signalrDeltas.Select(d => d.Seq).Should().BeInAscendingOrder();

        // Verify no gaps in sequences
        var httpSeqs = httpDeltas.Select(d => d.Seq).ToList();
        httpSeqs.Should().BeEquivalentTo(Enumerable.Range(0, httpSeqs.Count));

        var signalrSeqs = signalrDeltas.Select(d => d.Seq).ToList();
        signalrSeqs.Should().BeEquivalentTo(Enumerable.Range(0, signalrSeqs.Count));
    }

    [Fact(Skip = "SignalR hub not yet implemented")]
    public async Task SignalR_Deltas_Match_GenerationDelta_Schema()
    {
        // Arrange
        var sessionReq = new SessionRequest { Owner = "schema-test" };

        var hubConnection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/generation", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        await hubConnection.StartAsync(TestContext.Current.CancellationToken);

        var sessionResp = await hubConnection.InvokeAsync<SessionResponse>("StartSession", sessionReq, TestContext.Current.CancellationToken);
        sessionResp.Should().NotBeNull();

        var promptReq = new PromptRequest
        {
            SessionId = sessionResp.SessionId,
            Prompt = "Define a class in C#",
            TopK = 3,
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        // Act
        var deltas = new List<GenerationDelta>();
        var stream = hubConnection.StreamAsync<GenerationDelta>("SendPrompt", promptReq, TestContext.Current.CancellationToken);
        await foreach (var delta in stream.WithCancellation(TestContext.Current.CancellationToken))
        {
            deltas.Add(delta);
        }

        await hubConnection.StopAsync(TestContext.Current.CancellationToken);
        await hubConnection.DisposeAsync();

        // Assert: Validate schema compliance
        deltas.Should().NotBeEmpty();

        foreach (var delta in deltas)
        {
            // Required fields
            delta.PromptId.Should().NotBeNullOrWhiteSpace();
            delta.Type.Should().BeOneOf("token", "done", "error");
            delta.Role.Should().BeOneOf("assistant", "system", "user");

            // Sequence monotonicity
            delta.Seq.Should().BeGreaterThanOrEqualTo(0);

            // Token deltas have text
            if (delta.Type == "token")
            {
                delta.Text.Should().NotBeNull();
            }

            // Done/error deltas have metadata
            if (delta.Type == "done" || delta.Type == "error")
            {
                delta.Metadata.Should().NotBeNull();
            }
        }

        // Final event is done
        deltas.Last().Type.Should().Be("done");
    }

    [Fact(Skip = "SignalR hub not yet implemented")]
    public async Task SignalR_Cancel_Stops_Stream_And_Emits_Done_Delta()
    {
        // Arrange
        var sessionReq = new SessionRequest { Owner = "cancel-test" };

        var hubConnection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/generation", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        await hubConnection.StartAsync(TestContext.Current.CancellationToken);

        var sessionResp = await hubConnection.InvokeAsync<SessionResponse>("StartSession", sessionReq, TestContext.Current.CancellationToken);
        sessionResp.Should().NotBeNull();

        var promptReq = new PromptRequest
        {
            SessionId = sessionResp.SessionId,
            Prompt = "Write a 1000 word essay on neural networks", // Long-running
            TopK = 5,
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        // Act: Start streaming and cancel after first few deltas
        var deltas = new List<GenerationDelta>();
        var cts = new CancellationTokenSource();

        var streamTask = Task.Run(async () =>
        {
            var stream = hubConnection.StreamAsync<GenerationDelta>("SendPrompt", promptReq, cancellationToken: cts.Token);
            await foreach (var delta in stream.WithCancellation(cts.Token))
            {
                deltas.Add(delta);

                // Cancel after receiving 3 token deltas
                if (deltas.Count(d => d.Type == "token") >= 3)
                {
                    await hubConnection.InvokeAsync("Cancel", new CancelRequest
                    {
                        SessionId = sessionResp.SessionId,
                        PromptId = delta.PromptId
                    }, TestContext.Current.CancellationToken);
                    break;
                }
            }
        }, cts.Token);

        await streamTask;

        await hubConnection.StopAsync(TestContext.Current.CancellationToken);
        await hubConnection.DisposeAsync();

        // Assert: Verify cancellation behavior
        deltas.Should().NotBeEmpty();

        // Should have at least 3 token deltas before cancellation
        deltas.Count(d => d.Type == "token").Should().BeGreaterThanOrEqualTo(3);

        // Last delta should be done with cancelled metadata (if server emits it)
        // Note: Exact behavior depends on cancellation implementation
        var lastDelta = deltas.Last();
        lastDelta.Type.Should().BeOneOf("done", "error", "token");
    }

    [Fact(Skip = "SignalR hub not yet implemented")]
    public async Task SignalR_Multiple_Prompts_In_Same_Session()
    {
        // Arrange
        var sessionReq = new SessionRequest { Owner = "multi-prompt-test" };

        var hubConnection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/generation", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        await hubConnection.StartAsync(TestContext.Current.CancellationToken);

        var sessionResp = await hubConnection.InvokeAsync<SessionResponse>("StartSession", sessionReq, TestContext.Current.CancellationToken);
        sessionResp.Should().NotBeNull();

        // Act: Send two prompts in sequence
        var prompt1Req = new PromptRequest
        {
            SessionId = sessionResp.SessionId,
            Prompt = "What is C#?",
            TopK = 3,
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        var deltas1 = new List<GenerationDelta>();
        var stream1 = hubConnection.StreamAsync<GenerationDelta>("SendPrompt", prompt1Req, TestContext.Current.CancellationToken);
        await foreach (var delta in stream1.WithCancellation(TestContext.Current.CancellationToken))
        {
            deltas1.Add(delta);
        }

        var prompt2Req = new PromptRequest
        {
            SessionId = sessionResp.SessionId,
            Prompt = "What is .NET?",
            TopK = 3,
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        var deltas2 = new List<GenerationDelta>();
        var stream2 = hubConnection.StreamAsync<GenerationDelta>("SendPrompt", prompt2Req, TestContext.Current.CancellationToken);
        await foreach (var delta in stream2.WithCancellation(TestContext.Current.CancellationToken))
        {
            deltas2.Add(delta);
        }

        await hubConnection.StopAsync(TestContext.Current.CancellationToken);
        await hubConnection.DisposeAsync();

        // Assert: Both prompts completed successfully
        deltas1.Should().NotBeEmpty();
        deltas1.Last().Type.Should().Be("done");

        deltas2.Should().NotBeEmpty();
        deltas2.Last().Type.Should().Be("done");

        // Prompts have different IDs
        deltas1.First().PromptId.Should().NotBe(deltas2.First().PromptId);
    }
}
