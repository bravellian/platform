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

using Bravellian.Platform.Correlation;
using Shouldly;

namespace Bravellian.Platform.Audit.Tests;

public sealed class AuditModelTests
{
    [Fact]
    public void AnchorTrimsValues()
    {
        var anchor = new EventAnchor(" Tenant ", " 42 ", " Subject ");

        anchor.AnchorType.ShouldBe("Tenant");
        anchor.AnchorId.ShouldBe("42");
        anchor.Role.ShouldBe("Subject");
    }

    [Fact]
    public void ValidationRequiresNameMessageAndAnchor()
    {
        var auditEvent = new AuditEvent(
            new AuditEventId("evt-1"),
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            " ",
            " ",
            EventOutcome.Info,
            Array.Empty<EventAnchor>());

        var result = AuditEventValidator.Validate(auditEvent);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain("Event name is required.");
        result.Errors.ShouldContain("Display message is required.");
        result.Errors.ShouldContain("At least one anchor is required.");
    }

    [Fact]
    public void ValidationEnforcesDataJsonLimit()
    {
        var anchor = new EventAnchor("Invoice", "INV-1", "Subject");
        var auditEvent = new AuditEvent(
            new AuditEventId("evt-2"),
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "invoice.created",
            "Invoice created",
            EventOutcome.Success,
            new[] { anchor },
            new string('x', 10));

        var result = AuditEventValidator.Validate(auditEvent, new AuditValidationOptions { MaxDataJsonLength = 5 });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain("DataJson exceeds maximum length of 5 characters.");
    }

    [Fact]
    public void ValidationAcceptsValidEvent()
    {
        var anchor = new EventAnchor("Invoice", "INV-2", "Subject");
        var correlation = new CorrelationContext(
            new CorrelationId("corr-1"),
            null,
            "trace-1",
            "span-1",
            new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero));

        var auditEvent = new AuditEvent(
            AuditEventId.NewId(),
            new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
            "invoice.created",
            "Invoice created",
            EventOutcome.Success,
            new[] { anchor },
            "{ }",
            new AuditActor("System", "svc", "System"),
            correlation);

        var result = AuditEventValidator.Validate(auditEvent);

        result.IsValid.ShouldBeTrue();
        result.Errors.Count.ShouldBe(0);
    }
}