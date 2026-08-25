using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ReqSimulator.API.Services;

/// <summary>
/// Starts the GitHub Actions ingestion worker after a job has been queued.
/// The queue remains durable when dispatch is unavailable; the scheduled worker
/// is then the fallback rather than a prerequisite for every ingestion request.
/// </summary>
public sealed class IngestionWorkflowDispatcher
{
    private const string DefaultWorkflow = "ingestion-worker.yml";
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IngestionWorkflowDispatcher> _logger;

    public IngestionWorkflowDispatcher(
        HttpClient http,
        IConfiguration configuration,
        ILogger<IngestionWorkflowDispatcher> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> TryDispatchAsync(CancellationToken cancellationToken)
    {
        var token = _configuration["Ingestion:GitHubWorkflowToken"]?.Trim();
        var repository = _configuration["Ingestion:GitHubRepository"]?.Trim();
        if (string.IsNullOrWhiteSpace(token) || token.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(repository) || !repository.Contains('/', StringComparison.Ordinal))
        {
            _logger.LogInformation("Ingestion workflow dispatch is not configured; the scheduled worker remains available.");
            return false;
        }

        var workflow = _configuration["Ingestion:GitHubWorkflow"]?.Trim();
        var reference = _configuration["Ingestion:GitHubRef"]?.Trim();
        workflow = string.IsNullOrWhiteSpace(workflow) ? DefaultWorkflow : workflow;
        reference = string.IsNullOrWhiteSpace(reference) ? "main" : reference;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.github.com/repos/{repository}/actions/workflows/{Uri.EscapeDataString(workflow)}/dispatches")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { @ref = reference }), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("ReqSimulator-Ingestion-Dispatcher/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Requested ingestion workflow dispatch for repository {Repository}.", repository);
                return true;
            }

            _logger.LogWarning("Could not dispatch ingestion workflow. GitHub returned HTTP {StatusCode}.", (int)response.StatusCode);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Could not dispatch ingestion workflow; the queued job will be handled by the scheduled worker.");
        }

        return false;
    }
}
