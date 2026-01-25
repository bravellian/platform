using System.Net;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.HealthProbe;

public sealed class HttpHealthProbeRunner : IHealthProbeRunner
{
    private static readonly Action<ILogger, string, string, Exception?> LogProbeStart =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(1, "HealthProbeStart"),
            "Starting health probe for {Endpoint} at {Url}.");

    private static readonly Action<ILogger, int, double, Exception?> LogProbeResponse =
        LoggerMessage.Define<int, double>(
            LogLevel.Debug,
            new EventId(2, "HealthProbeResponse"),
            "Health probe response {StatusCode} in {ElapsedMilliseconds} ms.");

    private static readonly Action<ILogger, string, Exception?> LogProbeStatus =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(3, "HealthProbeStatus"),
            "Health probe JSON status is {Status}.");

    private static readonly Action<ILogger, double, Exception?> LogProbeTimeout =
        LoggerMessage.Define<double>(
            LogLevel.Warning,
            new EventId(4, "HealthProbeTimeout"),
            "Health probe timed out after {TimeoutSeconds} seconds.");

    private static readonly Action<ILogger, Exception?> LogProbeFailure =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(5, "HealthProbeFailure"),
            "Health probe failed with an exception.");

    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<HttpHealthProbeRunner> logger;
    private readonly HealthProbeOptions options;

    public HttpHealthProbeRunner(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpHealthProbeRunner> logger,
        HealthProbeOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        this.httpClientFactory = httpClientFactory;
        this.logger = logger;
        this.options = options;
    }

    public async Task<HealthProbeResult> RunAsync(HealthProbeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        LogProbeStart(logger, request.EndpointName, request.Url.ToString(), null);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.Url);
        if (!string.IsNullOrWhiteSpace(options.ApiKey) && !string.IsNullOrWhiteSpace(options.ApiKeyHeaderName))
        {
            httpRequest.Headers.TryAddWithoutValidation(options.ApiKeyHeaderName, options.ApiKey);
        }

        var clientName = options.AllowInsecureTls
            ? HealthProbeDefaults.HttpClientInsecureName
            : HealthProbeDefaults.HttpClientName;

        var client = httpClientFactory.CreateClient(clientName);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.Timeout);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await client.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token).ConfigureAwait(false);

            stopwatch.Stop();

            var statusCode = response.StatusCode;
            var isSuccessStatus = response.IsSuccessStatusCode;
            var isHealthy = isSuccessStatus;
            var jsonStatus = await TryReadJsonStatusAsync(response, timeoutCts.Token).ConfigureAwait(false);
            if (jsonStatus.HasValue && isSuccessStatus)
            {
                isHealthy = jsonStatus.Value;
            }

            var exitCode = isHealthy ? HealthProbeExitCodes.Healthy : HealthProbeExitCodes.Unhealthy;
            var message = BuildMessage(isHealthy, statusCode, jsonStatus, isSuccessStatus);

            LogProbeResponse(logger, (int)statusCode, stopwatch.Elapsed.TotalMilliseconds, null);

            return new HealthProbeResult(isHealthy, exitCode, message, statusCode, stopwatch.Elapsed);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            LogProbeTimeout(logger, options.Timeout.TotalSeconds, ex);
            return new HealthProbeResult(false, HealthProbeExitCodes.Exception, "Timeout", null, stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            LogProbeFailure(logger, ex);
            return new HealthProbeResult(false, HealthProbeExitCodes.Exception, ex.Message, null, stopwatch.Elapsed);
        }
    }

    private async Task<bool?> TryReadJsonStatusAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!LooksLikeJson(mediaType, content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (!TryGetStatusElement(document.RootElement, out var statusElement))
            {
                return null;
            }

            if (statusElement.ValueKind == JsonValueKind.String)
            {
                var status = statusElement.GetString() ?? string.Empty;
                LogProbeStatus(logger, status, null);
                return status.Equals("Healthy", StringComparison.OrdinalIgnoreCase);
            }

            LogProbeStatus(logger, statusElement.ValueKind.ToString(), null);
            return false;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool LooksLikeJson(string? mediaType, string content)
    {
        if (!string.IsNullOrWhiteSpace(mediaType) && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var trimmed = content.TrimStart();
        return trimmed.StartsWith("{", StringComparison.Ordinal);
    }

    private static bool TryGetStatusElement(JsonElement root, out JsonElement statusElement)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("status", out statusElement))
            {
                return true;
            }

            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.Equals("status", StringComparison.OrdinalIgnoreCase))
                {
                    statusElement = property.Value;
                    return true;
                }
            }
        }

        statusElement = default;
        return false;
    }

    private static string BuildMessage(bool isHealthy, HttpStatusCode statusCode, bool? jsonStatus, bool isSuccessStatus)
    {
        if (jsonStatus.HasValue && isSuccessStatus && !jsonStatus.Value)
        {
            return $"Unhealthy (status: {statusCode}, json: Unhealthy)";
        }

        return isHealthy
            ? $"Healthy ({(int)statusCode})"
            : $"Unhealthy ({(int)statusCode})";
    }
}
