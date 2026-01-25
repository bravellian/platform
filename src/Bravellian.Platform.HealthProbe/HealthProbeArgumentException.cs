using System.Runtime.Serialization;

namespace Bravellian.Platform.HealthProbe;

[Serializable]
internal sealed class HealthProbeArgumentException : Exception
{
    public HealthProbeArgumentException()
    {
    }

    public HealthProbeArgumentException(string message)
        : base(message)
    {
    }

    public HealthProbeArgumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    #pragma warning disable SYSLIB0051
    private HealthProbeArgumentException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
    #pragma warning restore SYSLIB0051
}
