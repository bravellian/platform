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

namespace Bravellian.Platform.Email.Postmark;

internal static class PostmarkWebhookEventTypes
{
    public const string Bounce = "bounce";
    public const string Suppression = "suppression";

    public static string? Map(string? recordType)
    {
        if (string.IsNullOrWhiteSpace(recordType))
        {
            return null;
        }

        if (string.Equals(recordType, "Bounce", StringComparison.OrdinalIgnoreCase))
        {
            return Bounce;
        }

        if (string.Equals(recordType, "SpamComplaint", StringComparison.OrdinalIgnoreCase))
        {
            return Suppression;
        }

        if (string.Equals(recordType, "SubscriptionChange", StringComparison.OrdinalIgnoreCase))
        {
            return Suppression;
        }

        return recordType;
    }
}
