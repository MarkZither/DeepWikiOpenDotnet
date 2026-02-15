using System;
using FluentAssertions;
using Xunit;
using DeepWiki.Rag.Core.Models;
using DeepWiki.Rag.Core.Services;

namespace DeepWiki.Rag.Core.Tests.Models
{
    public class SessionTests
    {
        [Fact]
        public void CreatedSession_HasValidTimestamps_AndActiveStatus()
        {
            var manager = new SessionManager();
            var session = manager.CreateSession("owner-x");

            session.Should().NotBeNull();
            session.SessionId.Should().NotBeNullOrEmpty();
            session.CreatedAt.Should().BeBefore(session.ExpiresAt);
            session.LastActiveAt.Should().BeOnOrAfter(session.CreatedAt);
            session.Status.Should().Be(SessionStatus.Active);
        }

        [Fact]
        public void ExpiredSession_Is_CleanedUp_By_Manager()
        {
            var manager = new SessionManager();
            var session = manager.CreateSession();

            // simulate expiration
            var s = manager.GetSession(session.SessionId)!;
            s.ExpiresAt = DateTime.UtcNow.AddHours(-2);

            manager.CleanupExpiredSessions();

            var after = manager.GetSession(session.SessionId);
            after.Should().BeNull();
        }
    }
}
