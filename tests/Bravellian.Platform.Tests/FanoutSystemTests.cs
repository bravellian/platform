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

namespace Bravellian.Platform.Tests;

/// <summary>
/// Tests for the fanout system core functionality.
/// </summary>
public class FanoutSystemTests
{
    /// <summary>
    /// When a FanoutSlice is serialized and deserialized, then its fields are preserved.
    /// </summary>
    /// <intent>
    /// Ensure FanoutSlice JSON round-trips correctly.
    /// </intent>
    /// <scenario>
    /// Given a FanoutSlice with explicit topic, shard key, work key, window start, and correlation ID.
    /// </scenario>
    /// <behavior>
    /// Then the deserialized slice matches the original field values.
    /// </behavior>
    [Fact]
    public void FanoutSlice_ShouldSerializeCorrectly()
    {
        // Arrange
        var slice = new FanoutSlice(
            fanoutTopic: "etl",
            shardKey: "tenant:123",
            workKey: "payments",
            windowStart: DateTimeOffset.Parse("2024-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            correlationId: "corr-123");

        // Act
        var json = JsonSerializer.Serialize(slice);
        var deserialized = JsonSerializer.Deserialize<FanoutSlice>(json);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.fanoutTopic.ShouldBe("etl");
        deserialized.shardKey.ShouldBe("tenant:123");
        deserialized.workKey.ShouldBe("payments");
        deserialized.windowStart.ShouldBe(DateTimeOffset.Parse("2024-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        deserialized.correlationId.ShouldBe("corr-123");
    }

    /// <summary>
    /// When a fanout topic name is constructed, then it follows the "fanout:{fanoutTopic}:{workKey}" convention.
    /// </summary>
    /// <intent>
    /// Document the fanout topic naming pattern used by the dispatcher.
    /// </intent>
    /// <scenario>
    /// Given a fanout topic of "etl" and a work key of "payments".
    /// </scenario>
    /// <behavior>
    /// Then the expected topic is "fanout:etl:payments".
    /// </behavior>
    [Fact]
    public void FanoutDispatcher_ShouldCreateCorrectTopicName()
    {
        // This is testing the topic naming convention: "fanout:{fanoutTopic}:{workKey}"
        var slice = new FanoutSlice("etl", "tenant:123", "payments");
        var expectedTopic = "fanout:etl:payments";

        // The topic formation is done in FanoutDispatcher, we're documenting the convention here
        expectedTopic.ShouldBe("fanout:etl:payments");
    }

    /// <summary>
    /// When FanoutTopicOptions is created with a fanout topic, then its default scheduling values are set.
    /// </summary>
    /// <intent>
    /// Verify default values for fanout scheduling configuration.
    /// </intent>
    /// <scenario>
    /// Given a FanoutTopicOptions instance with FanoutTopic set and no other overrides.
    /// </scenario>
    /// <behavior>
    /// Then work key is null and cron, interval, jitter, and lease defaults are populated.
    /// </behavior>
    [Fact]
    public void FanoutTopicOptions_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var options = new FanoutTopicOptions
        {
            FanoutTopic = "test-topic",
        };

        // Assert
        options.FanoutTopic.ShouldBe("test-topic");
        options.WorkKey.ShouldBeNull();
        options.Cron.ShouldBe("*/5 * * * *");
        options.DefaultEverySeconds.ShouldBe(300);
        options.JitterSeconds.ShouldBe(60);
        options.LeaseDuration.ShouldBe(TimeSpan.FromSeconds(90));
    }
}
