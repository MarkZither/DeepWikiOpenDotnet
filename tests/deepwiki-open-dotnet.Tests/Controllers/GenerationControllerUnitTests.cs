using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using DeepWiki.ApiService.Controllers;
using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Data.Abstractions;
using System.Threading;

namespace DeepWiki.ApiService.Tests.Controllers
{
    public class GenerationControllerUnitTests
    {
        [Fact]
        public void CreateSession_Returns_Created_WithSessionId()
        {
            var sessionManager = new DeepWiki.Rag.Core.Services.SessionManager();
            var svc = new TestGenerationService();
            var controller = new GenerationController(svc, sessionManager);

            var res = controller.CreateSession(new SessionRequest { Owner = "me" });
            var created = Assert.IsType<CreatedResult>(res.Result);
            var body = Assert.IsType<SessionResponse>(created.Value);
            body.SessionId.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task StreamGeneration_Writes_NDJSON_And_Completes()
        {
            var sessionManager = new DeepWiki.Rag.Core.Services.SessionManager();
            var svc = new TestGenerationService();
            var controller = new GenerationController(svc, sessionManager);

            var session = sessionManager.CreateSession();

            var ctx = new DefaultHttpContext();
            var ms = new MemoryStream();
            ctx.Response.Body = ms;
            controller.ControllerContext = new ControllerContext { HttpContext = ctx };

            var req = new PromptRequest { SessionId = session.SessionId, Prompt = "hello" };

            await controller.StreamGeneration(req);

            ms.Seek(0, SeekOrigin.Begin);
            var text = new StreamReader(ms).ReadToEnd();
            var lines = text.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
            lines.Length.Should().Be(3);

            var first = JsonSerializer.Deserialize<DeepWiki.Data.Abstractions.Models.GenerationDelta>(lines[0]);
            first!.Type.Should().Be("token");
            first.Text.Should().Be("a");

            var last = JsonSerializer.Deserialize<DeepWiki.Data.Abstractions.Models.GenerationDelta>(lines[2]);
            last!.Type.Should().Be("done");
        }

        [Fact]
        public async Task StreamGeneration_Writes_ErrorDelta_On_Exception()
        {
            var sessionManager = new DeepWiki.Rag.Core.Services.SessionManager();
            var svc = new ThrowingService();
            var controller = new GenerationController(svc, sessionManager);

            var session = sessionManager.CreateSession();

            var ctx = new DefaultHttpContext();
            var ms = new MemoryStream();
            ctx.Response.Body = ms;
            controller.ControllerContext = new ControllerContext { HttpContext = ctx };

            var req = new PromptRequest { SessionId = session.SessionId, Prompt = "hello" };

            await controller.StreamGeneration(req);

            ms.Seek(0, SeekOrigin.Begin);
            var text = new StreamReader(ms).ReadToEnd();
            var lines = text.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
            lines.Length.Should().Be(1);

            var err = JsonSerializer.Deserialize<DeepWiki.Data.Abstractions.Models.GenerationDelta>(lines[0]);
            err!.Type.Should().Be("error");
            ((JsonElement)err.Metadata!).GetProperty("code").GetString().Should().Be("internal_error");
        }
        [Fact]
        public async Task CancelGeneration_Returns_Ok()
        {
            var sessionManager = new DeepWiki.Rag.Core.Services.SessionManager();
            var svc = new TestGenerationService();
            var controller = new GenerationController(svc, sessionManager);

            var session = sessionManager.CreateSession();
            var prompt = sessionManager.CreatePrompt(session.SessionId, "x");

            var res = await controller.CancelGeneration(new CancelRequest { SessionId = session.SessionId, PromptId = prompt.PromptId });
            res.Should().BeOfType<OkResult>();
        }

        private class TestGenerationService : IGenerationService
        {
            public async IAsyncEnumerable<DeepWiki.Data.Abstractions.Models.GenerationDelta> GenerateAsync(string sessionId, string promptText, int topK = 5, System.Collections.Generic.Dictionary<string, string>? filters = null, string? idempotencyKey = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = sessionId, Role = "assistant", Type = "token", Seq = 0, Text = "a" };
                yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = sessionId, Role = "assistant", Type = "token", Seq = 1, Text = "b" };
                yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = sessionId, Role = "assistant", Type = "done", Seq = 2 };
            }

            public Task CancelAsync(string sessionId, string promptId)
            {
                return Task.CompletedTask;
            }
        }

        private class ThrowingService : IGenerationService
        {
            public IAsyncEnumerable<DeepWiki.Data.Abstractions.Models.GenerationDelta> GenerateAsync(string sessionId, string promptText, int topK = 5, System.Collections.Generic.Dictionary<string, string>? filters = null, string? idempotencyKey = null, CancellationToken cancellationToken = default)
            {
                return GenerateImpl();

                async IAsyncEnumerable<DeepWiki.Data.Abstractions.Models.GenerationDelta> GenerateImpl()
                {
                    // include a non-constant conditional yield to satisfy compiler requirement for async-iterator
                    if (DateTime.UtcNow.Ticks == 0) yield break;
                    await Task.Yield();
                    throw new System.InvalidOperationException("boom");
                }
            }

            public Task CancelAsync(string sessionId, string promptId) => Task.CompletedTask;
        }
    }
}
