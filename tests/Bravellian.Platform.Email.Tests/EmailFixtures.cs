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

namespace Bravellian.Platform.Email.Tests;

internal static class EmailFixtures
{
    public static OutboundEmailMessage CreateMessage(
        string subject = "Hello",
        string? textBody = "Hello there",
        string? htmlBody = null,
        string? messageKey = null)
    {
        return new OutboundEmailMessage(
            messageKey ?? Guid.NewGuid().ToString("n"),
            new EmailAddress("noreply@acme.test", "Acme"),
            new[] { new EmailAddress("user@acme.test") },
            subject,
            textBody,
            htmlBody);
    }
}
