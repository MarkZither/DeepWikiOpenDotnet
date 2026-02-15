using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using DeepWiki.Rag.Core.Services;

namespace DeepWiki.Rag.Core.Tests.Services
{
    public class SessionManagerTests
    {
        [Fact]
        public void CreateSession_Then_CreatePrompt_Works()
        {
            var manager = new SessionManager();
            var session = manager.CreateSession();

            var prompt = manager.CreatePrompt(session.SessionId, "hello");
            prompt.Should().NotBeNull();
            prompt.SessionId.Should().Be(session.SessionId);

            var fetched = manager.GetPrompt(session.SessionId, prompt.PromptId);
            fetched.Should().NotBeNull();
            fetched!.PromptId.Should().Be(prompt.PromptId);
        }

        [Fact]
        public void CreatePrompt_With_IdempotencyKey_ReturnsSamePrompt()
        {
            var manager = new SessionManager();
            var session = manager.CreateSession();

            var p1 = manager.CreatePrompt(session.SessionId, "x", "key-1");
            var p2 = manager.CreatePrompt(session.SessionId, "x", "key-1");

            p2.PromptId.Should().Be(p1.PromptId);
        }

        [Fact]
        public void CleanupExpiredSessions_Removes_IdempotencyKeys()
        {
            var manager = new SessionManager();
            var session = manager.CreateSession();

            var p1 = manager.CreatePrompt(session.SessionId, "x", "k-zip");
            // expire
            var s = manager.GetSession(session.SessionId)!;
            s.ExpiresAt = DateTime.UtcNow.AddHours(-5);

            manager.CleanupExpiredSessions();

            manager.GetSession(session.SessionId).Should().BeNull();

            // recreating a session with same id won't bring back idempotency key; ensure no exception
            var newSession = manager.CreateSession();
            var p2 = manager.CreatePrompt(newSession.SessionId, "x", "k-zip");
            p2.PromptId.Should().NotBe(p1.PromptId);
        }

        [Fact]
        public async Task Concurrent_CreatePrompt_IsThreadSafe()
        {
            var manager = new SessionManager();
            var session = manager.CreateSession();

            var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() => manager.CreatePrompt(session.SessionId, $"prompt-{i}"))).ToArray();
            await Task.WhenAll(tasks);

            // Ensure 20 prompts created
            var count = Enumerable.Range(0, 20).Select(i => tasks[i].Result.PromptId).Distinct().Count();
            count.Should().Be(20);
        }
    }
}
