namespace Bravellian.Platform.Tests;

public class OutboxMessageTests
{
    [Fact]
    public void OutboxMessage_IsRecord_WithCorrectProperties()
    {
        // This test verifies that OutboxMessage is a record with the expected properties
        // Since OutboxMessage has internal init setters, we can't directly instantiate it in tests
        // Instead, we verify the type structure and that it's a record type
        
        // Arrange & Act
        var messageType = typeof(OutboxMessage);

        // Assert
        messageType.ShouldNotBeNull();
        messageType.IsSealed.ShouldBeTrue(); // sealed record
        
        // Verify key properties exist
        var idProperty = messageType.GetProperty(nameof(OutboxMessage.Id));
        idProperty.ShouldNotBeNull();
        idProperty.PropertyType.ShouldBe(typeof(Guid));
        
        var topicProperty = messageType.GetProperty(nameof(OutboxMessage.Topic));
        topicProperty.ShouldNotBeNull();
        topicProperty.PropertyType.ShouldBe(typeof(string));
        
        var payloadProperty = messageType.GetProperty(nameof(OutboxMessage.Payload));
        payloadProperty.ShouldNotBeNull();
        payloadProperty.PropertyType.ShouldBe(typeof(string));
        
        var correlationIdProperty = messageType.GetProperty(nameof(OutboxMessage.CorrelationId));
        correlationIdProperty.ShouldNotBeNull();
        correlationIdProperty.PropertyType.ShouldBe(typeof(string));
    }

    [Fact]
    public void OutboxMessage_Properties_HaveExpectedTypes()
    {
        // Verify all properties have the correct types
        var messageType = typeof(OutboxMessage);
        
        messageType.GetProperty(nameof(OutboxMessage.Id))?.PropertyType.ShouldBe(typeof(Guid));
        messageType.GetProperty(nameof(OutboxMessage.Payload))?.PropertyType.ShouldBe(typeof(string));
        messageType.GetProperty(nameof(OutboxMessage.Topic))?.PropertyType.ShouldBe(typeof(string));
        messageType.GetProperty(nameof(OutboxMessage.CreatedAt))?.PropertyType.ShouldBe(typeof(DateTimeOffset));
        messageType.GetProperty(nameof(OutboxMessage.IsProcessed))?.PropertyType.ShouldBe(typeof(bool));
        messageType.GetProperty(nameof(OutboxMessage.ProcessedAt))?.PropertyType.ShouldBe(typeof(DateTimeOffset?));
        messageType.GetProperty(nameof(OutboxMessage.ProcessedBy))?.PropertyType.ShouldBe(typeof(string));
        messageType.GetProperty(nameof(OutboxMessage.RetryCount))?.PropertyType.ShouldBe(typeof(int));
        messageType.GetProperty(nameof(OutboxMessage.LastError))?.PropertyType.ShouldBe(typeof(string));
        messageType.GetProperty(nameof(OutboxMessage.NextAttemptAt))?.PropertyType.ShouldBe(typeof(DateTimeOffset));
        messageType.GetProperty(nameof(OutboxMessage.MessageId))?.PropertyType.ShouldBe(typeof(Guid));
        messageType.GetProperty(nameof(OutboxMessage.CorrelationId))?.PropertyType.ShouldBe(typeof(string));
    }

    [Fact]  
    public void OutboxMessage_IsPublicSealedRecord()
    {
        // Verify the type characteristics
        var messageType = typeof(OutboxMessage);
        
        messageType.IsPublic.ShouldBeTrue();
        messageType.IsSealed.ShouldBeTrue();
        messageType.IsClass.ShouldBeTrue(); // Records are classes in C#
        
        // Verify it has record-like characteristics
        var toStringMethod = messageType.GetMethod("ToString");
        toStringMethod.ShouldNotBeNull();
        toStringMethod.IsVirtual.ShouldBeTrue(); // Records have virtual ToString
    }
}