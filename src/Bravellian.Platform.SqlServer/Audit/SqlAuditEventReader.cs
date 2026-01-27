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

using System.Text;
using System.Text.Json;
using Bravellian.Platform.Audit;
using Bravellian.Platform.Correlation;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

/// <summary>
/// SQL Server implementation of <see cref="IAuditEventReader"/>.
/// </summary>
public sealed class SqlAuditEventReader : IAuditEventReader
{
    private readonly SqlAuditOptions options;
    private readonly ILogger<SqlAuditEventReader> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlAuditEventReader"/> class.
    /// </summary>
    /// <param name="options">SQL Server options.</param>
    /// <param name="logger">Logger instance.</param>
    public SqlAuditEventReader(IOptions<SqlAuditOptions> options, ILogger<SqlAuditEventReader> logger)
    {
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        var parameters = new DynamicParameters();
        var sql = new StringBuilder();
        sql.Append("SELECT DISTINCT e.* FROM [");
        sql.Append(options.SchemaName);
        sql.Append("].[" + options.AuditEventsTable + "] e");

        var hasAnchors = query.Anchors is { Count: > 0 };
        if (hasAnchors)
        {
            sql.Append(" INNER JOIN [");
            sql.Append(options.SchemaName);
            sql.Append("].[" + options.AuditAnchorsTable + "] a ON a.AuditEventId = e.AuditEventId");
        }

        var clauses = new List<string>();
        if (hasAnchors)
        {
            var anchorClauses = new List<string>();
            for (var i = 0; i < query.Anchors.Count; i++)
            {
                var anchor = query.Anchors[i];
                var typeName = "AnchorType" + i;
                var idName = "AnchorId" + i;
                var roleName = "AnchorRole" + i;

                anchorClauses.Add($"(a.AnchorType = @{typeName} AND a.AnchorId = @{idName} AND a.Role = @{roleName})");
                parameters.Add(typeName, anchor.AnchorType);
                parameters.Add(idName, anchor.AnchorId);
                parameters.Add(roleName, anchor.Role);
            }

            clauses.Add("(" + string.Join(" OR ", anchorClauses) + ")");
        }

        if (query.FromUtc is not null)
        {
            clauses.Add("e.OccurredAtUtc >= @FromUtc");
            parameters.Add("FromUtc", query.FromUtc);
        }

        if (query.ToUtc is not null)
        {
            clauses.Add("e.OccurredAtUtc <= @ToUtc");
            parameters.Add("ToUtc", query.ToUtc);
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            clauses.Add("e.Name = @Name");
            parameters.Add("Name", query.Name);
        }

        if (clauses.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.Append(string.Join(" AND ", clauses));
        }

        sql.Append(" ORDER BY e.OccurredAtUtc DESC");

        if (query.Limit is not null)
        {
            sql.Append(" OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY");
            parameters.Add("Limit", query.Limit);
        }

        var connection = new SqlConnection(options.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<AuditEventRow> events;
        try
        {
            events = (await connection.QueryAsync<AuditEventRow>(sql.ToString(), parameters).ConfigureAwait(false)).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to query audit events.");
            throw;
        }

        if (events.Count == 0)
        {
            SqlAuditMetrics.RecordRead(0);
            return Array.Empty<AuditEvent>();
        }

        var eventIds = events.Select(evt => evt.AuditEventId).ToArray();
        var anchorsSql = $"""
            SELECT AuditEventId, AnchorType, AnchorId, Role
            FROM [{options.SchemaName}].[{options.AuditAnchorsTable}]
            WHERE AuditEventId IN @AuditEventIds
            """;

        var anchors = await connection.QueryAsync<AuditAnchorRow>(anchorsSql, new { AuditEventIds = eventIds })
            .ConfigureAwait(false);

        var anchorLookup = anchors
            .GroupBy(anchor => anchor.AuditEventId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<EventAnchor>)group.Select(item => new EventAnchor(item.AnchorType, item.AnchorId, item.Role)).ToList(),
                StringComparer.Ordinal);

        var mapped = events.Select(evt => MapAuditEvent(evt, anchorLookup)).ToList();
        SqlAuditMetrics.RecordRead(mapped.Count);
        return mapped;
        }
    }

    private static AuditEvent MapAuditEvent(AuditEventRow row, IReadOnlyDictionary<string, IReadOnlyList<EventAnchor>> anchorLookup)
    {
        var anchors = anchorLookup.TryGetValue(row.AuditEventId, out var list)
            ? list
            : Array.Empty<EventAnchor>();

        var actor = row.ActorType is null || row.ActorId is null
            ? null
            : new AuditActor(row.ActorType, row.ActorId, row.ActorDisplay);

        CorrelationContext? correlation = null;
        if (!string.IsNullOrWhiteSpace(row.CorrelationId))
        {
            correlation = new CorrelationContext(
                new CorrelationId(row.CorrelationId),
                string.IsNullOrWhiteSpace(row.CausationId) ? null : new CorrelationId(row.CausationId),
                row.TraceId,
                row.SpanId,
                row.CorrelationCreatedAtUtc ?? DateTimeOffset.UnixEpoch,
                DeserializeTags(row.CorrelationTagsJson));
        }

        return new AuditEvent(
            new AuditEventId(row.AuditEventId),
            row.OccurredAtUtc,
            row.Name,
            row.DisplayMessage,
            row.Outcome,
            anchors,
            row.DataJson,
            actor,
            correlation);
    }

    private static IReadOnlyDictionary<string, string>? DeserializeTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
    }

    private sealed class AuditEventRow
    {
        public string AuditEventId { get; init; } = string.Empty;
        public DateTimeOffset OccurredAtUtc { get; init; }
        public string Name { get; init; } = string.Empty;
        public string DisplayMessage { get; init; } = string.Empty;
        public EventOutcome Outcome { get; init; }
        public string? DataJson { get; init; }
        public string? ActorType { get; init; }
        public string? ActorId { get; init; }
        public string? ActorDisplay { get; init; }
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public string? TraceId { get; init; }
        public string? SpanId { get; init; }
        public DateTimeOffset? CorrelationCreatedAtUtc { get; init; }
        public string? CorrelationTagsJson { get; init; }
    }

    private sealed class AuditAnchorRow
    {
        public string AuditEventId { get; init; } = string.Empty;
        public string AnchorType { get; init; } = string.Empty;
        public string AnchorId { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
    }
}
