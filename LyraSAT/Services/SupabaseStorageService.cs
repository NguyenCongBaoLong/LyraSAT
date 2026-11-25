using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace LyraSAT.Services
{
    public sealed class StorageFileTooLargeException : Exception
    {
        public StorageFileTooLargeException(string message) : base(message)
        {
        }
    }

    public interface ISupabaseStorageService
    {
        Task<string> UploadAsync(byte[] content, string objectPath, string contentType, CancellationToken cancellationToken = default);
        Task<string> UploadStreamAsync(Stream content, long? contentLength, string objectPath, string contentType, string? bucketName = null, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(string objectPath, CancellationToken cancellationToken = default);
        Task<bool> DeleteByUrlAsync(string? fileUrl, CancellationToken cancellationToken = default);
        string? TryExtractObjectPath(string? fileUrl);
        (string Bucket, string ObjectPath)? TryExtractBucketAndPath(string? fileUrl);
    }

    public sealed class SupabaseStorageService : ISupabaseStorageService
    {
        private const string BucketName = "question-assets";
        public const string MediaBucket = "media-assets";
        private static readonly ConcurrentDictionary<string, bool> VerifiedBuckets = new(StringComparer.OrdinalIgnoreCase);

        private readonly HttpClient _httpClient;
        private readonly string _supabaseUrl;
        private readonly string _serviceRoleKey;
        private readonly ILogger<SupabaseStorageService> _logger;

        public SupabaseStorageService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<SupabaseStorageService> logger)
        {
            _httpClient = httpClient;
            _supabaseUrl = (
                configuration["Supabase:Url"] ??
                configuration["SUPABASE_URL"])?.TrimEnd('/')
                ?? throw new InvalidOperationException("Supabase URL is not configured.");
            _serviceRoleKey =
                configuration["Supabase:ServiceRoleKey"] ??
                configuration["SUPABASE_SERVICE_ROLE_KEY"]
                ?? throw new InvalidOperationException("Supabase service role key is not configured.");
            _logger = logger;
        }

        private async Task EnsurePublicBucketAsync(string bucketName, CancellationToken cancellationToken)
        {
            if (VerifiedBuckets.ContainsKey(bucketName)) return;

            using var checkRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_supabaseUrl}/storage/v1/bucket/{bucketName}");
            checkRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceRoleKey);
            checkRequest.Headers.Add("apikey", _serviceRoleKey);

            using var checkResponse = await _httpClient.SendAsync(checkRequest, cancellationToken);
            if (checkResponse.IsSuccessStatusCode)
            {
                VerifiedBuckets[bucketName] = true;
                return;
            }

            var checkBody = await checkResponse.Content.ReadAsStringAsync(cancellationToken);
            bool bucketMissing = checkResponse.StatusCode == HttpStatusCode.NotFound ||
                (checkResponse.StatusCode == HttpStatusCode.BadRequest &&
                 checkBody.Contains("not found", StringComparison.OrdinalIgnoreCase));

            if (!bucketMissing)
            {
                _logger.LogError(
                    "Supabase bucket check failed. Bucket={Bucket}, Status={Status}, Response={Response}",
                    bucketName,
                    (int)checkResponse.StatusCode,
                    checkBody);
                throw new InvalidOperationException($"Supabase bucket check failed with status {(int)checkResponse.StatusCode}.");
            }

            var payload = $"{{\"id\":\"{bucketName}\",\"name\":\"{bucketName}\",\"public\":true}}";
            using var createRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_supabaseUrl}/storage/v1/bucket");
            createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceRoleKey);
            createRequest.Headers.Add("apikey", _serviceRoleKey);
            createRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var createResponse = await _httpClient.SendAsync(createRequest, cancellationToken);
            var createBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            if (createResponse.IsSuccessStatusCode ||
                createResponse.StatusCode == HttpStatusCode.Conflict ||
                createBody.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                VerifiedBuckets[bucketName] = true;
                return;
            }

            _logger.LogError(
                "Supabase bucket create failed. Bucket={Bucket}, Status={Status}, Response={Response}",
                bucketName,
                (int)createResponse.StatusCode,
                createBody);
            throw new InvalidOperationException($"Supabase bucket create failed with status {(int)createResponse.StatusCode}.");
        }

        public async Task<string> UploadAsync(
            byte[] content,
            string objectPath,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream(content, writable: false);
            return await UploadStreamAsync(ms, content.LongLength, objectPath, contentType, BucketName, cancellationToken);
        }

        public async Task<string> UploadStreamAsync(
            Stream content,
            long? contentLength,
            string objectPath,
            string contentType,
            string? bucketName = null,
            CancellationToken cancellationToken = default)
        {
            var targetBucket = string.IsNullOrWhiteSpace(bucketName) ? BucketName : bucketName.Trim();
            if (!targetBucket.Equals(BucketName, StringComparison.OrdinalIgnoreCase))
            {
                await EnsurePublicBucketAsync(targetBucket, cancellationToken);
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_supabaseUrl}/storage/v1/object/{targetBucket}/{objectPath}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceRoleKey);
            request.Headers.Add("apikey", _serviceRoleKey);
            request.Headers.Add("x-upsert", "true");

            var streamContent = new StreamContent(content);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            if (contentLength.HasValue && contentLength.Value >= 0)
            {
                streamContent.Headers.ContentLength = contentLength.Value;
            }
            request.Content = streamContent;

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Supabase Storage upload failed. Bucket={Bucket}, Status={Status}, Path={Path}, Response={Response}",
                    targetBucket,
                    (int)response.StatusCode,
                    objectPath,
                    responseBody);

                bool isTooLarge = response.StatusCode == HttpStatusCode.RequestEntityTooLarge ||
                    responseBody.Contains("Payload too large", StringComparison.OrdinalIgnoreCase) ||
                    responseBody.Contains("exceeded the maximum allowed size", StringComparison.OrdinalIgnoreCase) ||
                    responseBody.Contains("\"413\"", StringComparison.Ordinal);
                if (isTooLarge)
                {
                    throw new StorageFileTooLargeException("File vượt giới hạn dung lượng của dịch vụ lưu trữ đám mây.");
                }

                throw new InvalidOperationException($"Supabase Storage upload failed with status {(int)response.StatusCode}.");
            }

            return $"{_supabaseUrl}/storage/v1/object/public/{targetBucket}/{objectPath}";
        }

        public (string Bucket, string ObjectPath)? TryExtractBucketAndPath(string? fileUrl)
        {
            if (string.IsNullOrWhiteSpace(fileUrl)) return null;
            const string prefix = "/storage/v1/object/public/";
            int idx = fileUrl.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            var tail = fileUrl[(idx + prefix.Length)..];
            int qIdx = tail.IndexOfAny(['?', '#']);
            if (qIdx >= 0) tail = tail[..qIdx];
            tail = tail.TrimStart('/');

            int slashIdx = tail.IndexOf('/');
            if (slashIdx <= 0 || slashIdx >= tail.Length - 1) return null;

            var bucket = tail[..slashIdx];
            var objectPath = tail[(slashIdx + 1)..].TrimStart('/');
            if (bucket.Length == 0 || objectPath.Length == 0) return null;
            return (bucket, objectPath);
        }

        public string? TryExtractObjectPath(string? fileUrl)
        {
            var extracted = TryExtractBucketAndPath(fileUrl);
            if (extracted == null) return null;
            return extracted.Value.Bucket.Equals(BucketName, StringComparison.OrdinalIgnoreCase)
                ? extracted.Value.ObjectPath
                : null;
        }

        public Task<bool> DeleteAsync(string objectPath, CancellationToken cancellationToken = default) =>
            DeleteFromBucketAsync(BucketName, objectPath, cancellationToken);

        public Task<bool> DeleteByUrlAsync(string? fileUrl, CancellationToken cancellationToken = default)
        {
            var extracted = TryExtractBucketAndPath(fileUrl);
            if (extracted == null) return Task.FromResult(false);
            return DeleteFromBucketAsync(extracted.Value.Bucket, extracted.Value.ObjectPath, cancellationToken);
        }

        private async Task<bool> DeleteFromBucketAsync(string bucketName, string objectPath, CancellationToken cancellationToken)
        {
            var normalized = (objectPath ?? string.Empty).TrimStart('/');
            if (normalized.Length == 0) return false;

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Delete,
                    $"{_supabaseUrl}/storage/v1/object/{bucketName}/{normalized}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceRoleKey);
                request.Headers.Add("apikey", _serviceRoleKey);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                {
                    _logger.LogWarning(
                        "Supabase Storage delete returned {Status} for {Bucket}/{Path}",
                        (int)response.StatusCode,
                        bucketName,
                        normalized);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Supabase Storage delete failed for {Bucket}/{Path}", bucketName, normalized);
                return false;
            }
        }
    }
}
