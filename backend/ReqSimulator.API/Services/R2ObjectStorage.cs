using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace ReqSimulator.API.Services;

public interface IR2ObjectStorage
{
    Task<string> CreateUploadUrlAsync(string objectKey, string contentType, CancellationToken cancellationToken);
    Task<string> CreateDownloadUrlAsync(string objectKey, CancellationToken cancellationToken);
    Task<long> GetObjectLengthAsync(string objectKey, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}

/// <summary>Small S3-compatible adapter for a private Cloudflare R2 bucket.</summary>
public sealed class R2ObjectStorage : IR2ObjectStorage
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<R2ObjectStorage> _logger;

    public R2ObjectStorage(IConfiguration configuration, ILogger<R2ObjectStorage> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task<string> CreateUploadUrlAsync(string objectKey, string contentType, CancellationToken cancellationToken) =>
        CreateUrlAsync(objectKey, HttpVerb.PUT, contentType, cancellationToken);

    public Task<string> CreateDownloadUrlAsync(string objectKey, CancellationToken cancellationToken) =>
        CreateUrlAsync(objectKey, HttpVerb.GET, null, cancellationToken);

    public async Task<long> GetObjectLengthAsync(string objectKey, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        var response = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = GetRequired("R2:Bucket"),
            Key = objectKey,
        }, cancellationToken);
        return response.ContentLength;
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        await client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = GetRequired("R2:Bucket"), Key = objectKey }, cancellationToken);
    }

    private Task<string> CreateUrlAsync(string objectKey, HttpVerb verb, string? contentType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var client = CreateClient();
        var request = new GetPreSignedUrlRequest
        {
            BucketName = GetRequired("R2:Bucket"),
            Key = objectKey,
            Verb = verb,
            Expires = DateTime.UtcNow.AddMinutes(10),
        };
        if (!string.IsNullOrWhiteSpace(contentType)) request.ContentType = contentType;
        return Task.FromResult(client.GetPreSignedURL(request));
    }

    private IAmazonS3 CreateClient()
    {
        var endpoint = GetRequired("R2:ServiceUrl");
        var accessKey = GetRequired("R2:AccessKeyId");
        var secret = GetRequired("R2:SecretAccessKey");
        return new AmazonS3Client(new BasicAWSCredentials(accessKey, secret), new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
        });
    }

    private string GetRequired(string key)
    {
        var value = _configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("R2 is not configured; ingestion upload cannot be started.");
            throw new InvalidOperationException("Object storage is not configured.");
        }
        return value;
    }
}
