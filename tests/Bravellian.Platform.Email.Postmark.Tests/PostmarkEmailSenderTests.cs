// Copyright (c) Bravellian
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Net;
using System.Text;
using System.Text.Json;
using Bravellian.Platform.Email;
using Bravellian.Platform.Email.Postmark;

namespace Bravellian.Platform.Email.Postmark.Tests;

public sealed class PostmarkEmailSenderTests
{
    [Fact]
    public async Task SendAsync_SendsExpectedPayload()
    {
        using var handler = new CapturingHandler(HttpStatusCode.OK, "{\"MessageID\":\"abc-123\"}");
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.postmarkapp.com/")
        };
        var sender = new PostmarkEmailSender(httpClient, new PostmarkOptions
        {
            ServerToken = "server-token",
            MessageStream = "transactional"
        });
        var message = new OutboundEmailMessage(
            "key-1",
            new EmailAddress("sender@acme.test"),
            new[] { new EmailAddress("recipient@acme.test") },
            "Greetings",
            "Hello there",
            null,
            headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Custom"] = "Value" });

        var result = await sender.SendAsync(message, CancellationToken.None);

        result.Status.ShouldBe(EmailDeliveryStatus.Sent);
        result.ProviderMessageId.ShouldBe("abc-123");
        handler.Request.ShouldNotBeNull();
        handler.Request!.Method.ShouldBe(HttpMethod.Post);
        handler.Request.RequestUri!.AbsoluteUri.ShouldBe("https://api.postmarkapp.com/email");
        handler.Request.Headers.Contains("X-Postmark-Server-Token").ShouldBeTrue();
        handler.RequestBody.ShouldNotBeNull();

        using var json = JsonDocument.Parse(handler.RequestBody!);
        json.RootElement.GetProperty("From").GetString().ShouldBe("sender@acme.test");
        json.RootElement.GetProperty("To").GetString().ShouldBe("recipient@acme.test");
        json.RootElement.GetProperty("Subject").GetString().ShouldBe("Greetings");
        json.RootElement.GetProperty("TextBody").GetString().ShouldBe("Hello there");
        json.RootElement.GetProperty("MessageStream").GetString().ShouldBe("transactional");

        var headers = json.RootElement.GetProperty("Headers").EnumerateArray().ToArray();
        headers.Any(header => string.Equals(header.GetProperty("Name").GetString(), "X-Custom", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
        headers.Any(header => string.Equals(header.GetProperty("Name").GetString(), "X-Message-Key", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();

        var metadata = json.RootElement.GetProperty("Metadata");
        metadata.GetProperty("MessageKey").GetString().ShouldBe("key-1");
    }

    [Fact]
    public async Task SendAsync_ReportsTransientFailureOnServerError()
    {
        using var handler = new CapturingHandler(HttpStatusCode.InternalServerError, "{\"Message\":\"boom\"}");
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.postmarkapp.com/")
        };
        var sender = new PostmarkEmailSender(httpClient, new PostmarkOptions { ServerToken = "server-token" });
        var message = new OutboundEmailMessage(
            "key-2",
            new EmailAddress("sender@acme.test"),
            new[] { new EmailAddress("recipient@acme.test") },
            "Greetings",
            "Hello there",
            null);

        var result = await sender.SendAsync(message, CancellationToken.None);

        result.Status.ShouldBe(EmailDeliveryStatus.FailedTransient);
        result.FailureType.ShouldBe(EmailFailureType.Transient);
        result.ErrorMessage?.ShouldContain("boom");
    }

    [Fact]
    public async Task SendAsync_ReportsPermanentFailureOnBadRequest()
    {
        using var handler = new CapturingHandler(HttpStatusCode.BadRequest, "{\"Message\":\"invalid\"}");
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.postmarkapp.com/")
        };
        var sender = new PostmarkEmailSender(httpClient, new PostmarkOptions { ServerToken = "server-token" });
        var message = new OutboundEmailMessage(
            "key-3",
            new EmailAddress("sender@acme.test"),
            new[] { new EmailAddress("recipient@acme.test") },
            "Greetings",
            "Hello there",
            null);

        var result = await sender.SendAsync(message, CancellationToken.None);

        result.Status.ShouldBe(EmailDeliveryStatus.FailedPermanent);
        result.FailureType.ShouldBe(EmailFailureType.Permanent);
        result.ErrorMessage?.ShouldContain("invalid");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode statusCode;
        private readonly string responseBody;

        public CapturingHandler(HttpStatusCode statusCode, string responseBody)
        {
            this.statusCode = statusCode;
            this.responseBody = responseBody;
        }

        public HttpRequestMessage? Request { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content != null)
            {
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
