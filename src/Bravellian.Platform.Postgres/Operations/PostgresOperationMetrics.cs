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
using Bravellian.Platform.Operations;

namespace Bravellian.Platform;

internal static class PostgresOperationMetrics
{
    private static readonly PlatformMeterProvider MeterProvider = new(
        "Bravellian.Platform.Operations",
        "1.0.0");
    private static readonly Meter Meter = MeterProvider.Meter;

    private static readonly Counter<long> OperationStartedTotal =
        Meter.CreateCounter<long>("bravellian.platform.operations.started_total", unit: "items", description: "Total number of operations started.");

    private static readonly Counter<long> OperationProgressUpdatedTotal =
        Meter.CreateCounter<long>("bravellian.platform.operations.progress_updated_total", unit: "items", description: "Total number of operation progress updates.");

    private static readonly Counter<long> OperationEventAddedTotal =
        Meter.CreateCounter<long>("bravellian.platform.operations.event_added_total", unit: "items", description: "Total number of operation events appended.");

    private static readonly Counter<long> OperationCompletedTotal =
        Meter.CreateCounter<long>("bravellian.platform.operations.completed_total", unit: "items", description: "Total number of operations completed.");

    private static readonly Counter<long> OperationSnapshotReadTotal =
        Meter.CreateCounter<long>("bravellian.platform.operations.snapshot_read_total", unit: "items", description: "Total number of operation snapshots retrieved.");

    public static void RecordStarted()
    {
        OperationStartedTotal.Add(1);
    }

    public static void RecordProgressUpdated()
    {
        OperationProgressUpdatedTotal.Add(1);
    }

    public static void RecordEventAdded(string kind)
    {
        OperationEventAddedTotal.Add(1, new KeyValuePair<string, object?>("kind", kind));
    }

    public static void RecordCompleted(OperationStatus status)
    {
        OperationCompletedTotal.Add(1, new KeyValuePair<string, object?>("status", status.ToString()));
    }

    public static void RecordSnapshotRead(bool found)
    {
        OperationSnapshotReadTotal.Add(1, new KeyValuePair<string, object?>("found", found));
    }
}
