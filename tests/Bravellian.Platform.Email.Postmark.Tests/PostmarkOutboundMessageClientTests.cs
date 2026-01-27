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
using Bravellian.Platform.Email.Postmark;

namespace Bravellian.Platform.Email.Postmark.Tests;

public sealed class PostmarkOutboundMessageClientTests
{
    [Fact]
    public async Task GetOutboundMessageDetailsAsync_ReturnsNotFoundOn404()
    {
        var handler = new CapturingHandler(HttpStatusCode.NotFound, string.Empty);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.postmarkapp.com/")
        };
        var client = new PostmarkOutboundMessageClient(httpClient, new PostmarkOptions { ServerToken = "server-token" });

        var result = await client.GetOutboundMessageDetailsAsync("missing", CancellationToken.None);

        result.Status.ShouldBe(PostmarkOutboundMessageClient.PostmarkQueryStatus.NotFound);
    }

    [Fact]
    public async Task SearchOutboundByMetadataAsync_ReturnsNotFoundWhenEmpty()
    {
        var payload = "{\"TotalCount\":0,\"Messages\":[]}";
        var handler = new CapturingHandler(HttpStatusCode.OK, payload);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.postmarkapp.com/")
        };
        var client = new PostmarkOutboundMessageClient(httpClient, new PostmarkOptions { ServerToken = "server-token" });

        var result = await client.SearchOutboundByMetadataAsync("MessageKey", "value", CancellationToken.None);

        result.Status.ShouldBe(PostmarkOutboundMessageClient.PostmarkQueryStatus.NotFound);
    }

    [Fact]
    public async Task SearchOutboundByMetadataAsync_ReturnsFoundWhenMessageExists()
    {
        var payload = "{\"TotalCount\":1,\"Messages\":[{\"MessageID\":\"abc\",\"Status\":\"Sent\"}]}";
        var handler = new CapturingHandler(HttpStatusCode.OK, payload);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.postmarkapp.com/")
        };
        var client = new PostmarkOutboundMessageClient(httpClient, new PostmarkOptions { ServerToken = "server-token" });

        var result = await client.SearchOutboundByMetadataAsync("MessageKey", "value", CancellationToken.None);

        result.Status.ShouldBe(PostmarkOutboundMessageClient.PostmarkQueryStatus.Found);
        result.Response!.Messages!.Count.ShouldBe(1);
        result.Response.Messages[0].MessageId.ShouldBe("abc");
    }

    [Fact]
    public async Task SearchOutboundByMetadataAsync_ReturnsErrorOnFailure()
    {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError, "{\"Message\":\"boom\"}");
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.postmarkapp.com/")
        };
        var client = new PostmarkOutboundMessageClient(httpClient, new PostmarkOptions { ServerToken = "server-token" });

        var result = await client.SearchOutboundByMetadataAsync("MessageKey", "value", CancellationToken.None);

        result.Status.ShouldBe(PostmarkOutboundMessageClient.PostmarkQueryStatus.Error);
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
