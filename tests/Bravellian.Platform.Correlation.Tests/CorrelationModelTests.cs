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

using Shouldly;

namespace Bravellian.Platform.Correlation.Tests;

public sealed class CorrelationModelTests
{
    [Fact]
    public void CorrelationIdRequiresValue()
    {
        Should.Throw<ArgumentException>(() => new CorrelationId(" "));
    }

    [Fact]
    public void CorrelationIdTryParseRejectsEmpty()
    {
        CorrelationId.TryParse(null, out _).ShouldBeFalse();
        CorrelationId.TryParse(" ", out _).ShouldBeFalse();
    }

    [Fact]
    public void GeneratorReturnsValue()
    {
        var generator = new DefaultCorrelationIdGenerator();
        var id = generator.NewId();

        id.Value.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GeneratorCanBeDeterministic()
    {
        var generator = new FixedCorrelationIdGenerator(new CorrelationId("corr-fixed"));

        generator.NewId().Value.ShouldBe("corr-fixed");
        generator.NewId().Value.ShouldBe("corr-fixed");
    }

    [Fact]
    public void AmbientAccessorStoresContext()
    {
        var accessor = new AmbientCorrelationContextAccessor();
        var context = new CorrelationContext(
            new CorrelationId("corr-1"),
            new CorrelationId("cause-1"),
            "trace-1",
            "span-1",
            new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));

        accessor.Current = context;

        accessor.Current.ShouldBe(context);
    }

    [Fact]
    public async Task AmbientAccessorFlowsAcrossAsync()
    {
        var accessor = new AmbientCorrelationContextAccessor();
        var context = new CorrelationContext(
            new CorrelationId("corr-async"),
            null,
            null,
            null,
            new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));

        accessor.Current = context;

        CorrelationContext? observed = null;
        await Task.Run(() => observed = accessor.Current);

        observed.ShouldBe(context);
    }

    [Fact]
    public void ScopeRestoresPreviousContext()
    {
        var accessor = new AmbientCorrelationContextAccessor();
        var previous = new CorrelationContext(
            new CorrelationId("corr-prev"),
            null,
            null,
            null,
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var next = new CorrelationContext(
            new CorrelationId("corr-next"),
            null,
            null,
            null,
            new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero));

        accessor.Current = previous;

        using (new CorrelationScope(accessor, next))
        {
            accessor.Current.ShouldBe(next);
        }

        accessor.Current.ShouldBe(previous);
    }

    [Fact]
    public void SerializerRoundTrips()
    {
        var serializer = new DefaultCorrelationSerializer();
        var createdAt = new DateTimeOffset(2024, 3, 1, 8, 30, 0, TimeSpan.Zero);
        var context = new CorrelationContext(
            new CorrelationId("corr-serialize"),
            new CorrelationId("cause-serialize"),
            "trace-serialize",
            "span-serialize",
            createdAt,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["tenant"] = "bravellian" });

        var values = serializer.Serialize(context);
        var result = serializer.Deserialize(values);

        result.ShouldNotBeNull();
        result!.CorrelationId.ShouldBe(context.CorrelationId);
        result.CausationId.ShouldBe(context.CausationId);
        result.TraceId.ShouldBe(context.TraceId);
        result.SpanId.ShouldBe(context.SpanId);
        result.CreatedAtUtc.ShouldBe(createdAt);
        result.Tags.ShouldNotBeNull();
        result.Tags!["tenant"].ShouldBe("bravellian");
    }

    private sealed class FixedCorrelationIdGenerator : ICorrelationIdGenerator
    {
        private readonly CorrelationId id;

        public FixedCorrelationIdGenerator(CorrelationId id)
        {
            this.id = id;
        }

        public CorrelationId NewId() => id;
    }
}
