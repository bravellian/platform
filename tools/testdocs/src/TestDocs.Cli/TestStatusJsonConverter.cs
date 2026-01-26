using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestDocs.Cli;

internal sealed class TestStatusJsonConverter : JsonConverter<TestStatus>
{
    public override TestStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotSupportedException("Deserialization is not supported.");
    }

    public override void Write(Utf8JsonWriter writer, TestStatus value, JsonSerializerOptions options)
    {
        var text = value switch
        {
            TestStatus.Compliant => "compliant",
            TestStatus.MissingRequired => "missing-required",
            TestStatus.InvalidFormat => "invalid-format",
            _ => "unknown",
        };
        writer.WriteStringValue(text);
    }
}
