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

using System.Diagnostics.Metrics;
using Bravellian.Platform.Metrics;

namespace Bravellian.Platform;

internal static class PostgresAuditMetrics
{
    private static readonly PlatformMeterProvider MeterProvider = new(
        "Bravellian.Platform.Audit",
        "1.0.0");
    private static readonly Meter Meter = MeterProvider.Meter;

    private static readonly Counter<long> AuditWrittenTotal =
        Meter.CreateCounter<long>("bravellian.platform.audit.written_total", unit: "items", description: "Total number of audit events written.");

    private static readonly Counter<long> AuditReadTotal =
        Meter.CreateCounter<long>("bravellian.platform.audit.read_total", unit: "items", description: "Total number of audit events read.");

    public static void RecordWritten(string? outcome)
    {
        AuditWrittenTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public static void RecordRead(int count)
    {
        AuditReadTotal.Add(count);
    }
}
