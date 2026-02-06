using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DeepWiki.Data.Abstractions.Models;

namespace deepwiki_open_dotnet.Tests.Models
{
    public class GenerationDTOTests
    {
        [Fact]
        public void GenerationDelta_Serializes_And_Deserializes()
        {
            var delta = new GenerationDelta
            {
                PromptId = "p-1",
                Type = "token",
                Seq = 0,
                Text = "hello",
                Role = "assistant",
                Metadata = new { foo = "bar" }
            };

            var json = JsonSerializer.Serialize(delta);
            var roundTrip = JsonSerializer.Deserialize<GenerationDelta>(json)!;

            roundTrip.PromptId.Should().Be(delta.PromptId);
            roundTrip.Type.Should().Be(delta.Type);
            roundTrip.Seq.Should().Be(delta.Seq);
            roundTrip.Text.Should().Be(delta.Text);
            roundTrip.Role.Should().Be(delta.Role);
        }

        [Fact]
        public void PromptRequest_Validation_Fails_For_Missing_Fields()
        {
            var invalid = new PromptRequest { SessionId = "", Prompt = "" };
            var ctx = new ValidationContext(invalid);
            var results = new List<ValidationResult>();

            var valid = Validator.TryValidateObject(invalid, ctx, results, true);

            valid.Should().BeFalse();
            results.Should().Contain(r => r.ErrorMessage!.Contains("SessionId") || r.ErrorMessage!.Contains("Prompt"));
        }

        [Fact]
        public void SessionRequest_And_Response_Serializes()
        {
            var req = new SessionRequest { Owner = "me", Context = new Dictionary<string, string>{{"k","v"}} };
            var json = JsonSerializer.Serialize(req);
            var r = JsonSerializer.Deserialize<SessionRequest>(json)!;
            r.Owner.Should().Be(req.Owner);
            r.Context.Should().ContainKey("k");

            var resp = new SessionResponse { SessionId = "s-1" };
            var j2 = JsonSerializer.Serialize(resp);
            var r2 = JsonSerializer.Deserialize<SessionResponse>(j2)!;
            r2.SessionId.Should().Be(resp.SessionId);
        }

        [Fact]
        public void CancelRequest_Validation_Fails_For_Missing_Fields()
        {
            var invalid = new CancelRequest { SessionId = "", PromptId = "" };
            var ctx = new ValidationContext(invalid);
            var results = new List<ValidationResult>();

            var valid = Validator.TryValidateObject(invalid, ctx, results, true);

            valid.Should().BeFalse();
            results.Should().Contain(r => r.ErrorMessage!.Contains("SessionId") || r.ErrorMessage!.Contains("PromptId"));
        }
    }
}
