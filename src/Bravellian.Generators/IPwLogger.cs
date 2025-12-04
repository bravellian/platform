using System;

namespace Bravellian.Generators;

public interface IBgLogger
{
    void LogMessage(string message);
    void LogWarning(string message);
    void LogError(string message);
    void LogError(string message, Exception ex);
    void LogErrorFromException(Exception ex);
}
