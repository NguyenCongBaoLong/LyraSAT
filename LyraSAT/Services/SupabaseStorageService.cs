using System.Net.Http.Headers;

namespace LyraSAT.Services
{
    public interface ISupabaseStorageService
    {
        Task<string> UploadAsync(byte[] content, string objectPath, string contentType, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(string objectPath, CancellationToken cancellationToken = default);
        string? TryExtractObjectPath(string? fileUrl);
    }

    public sealed class SupabaseStorageService : ISupabaseStorageService
    {
        private const string BucketName = "question-assets";
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

        public async Task<string> UploadAsync(
            byte[] content,
            string objectPath,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_supabaseUrl}/storage/v1/object/{BucketName}/{objectPath}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceRoleKey);
            request.Headers.Add("apikey", _serviceRoleKey);
            request.Headers.Add("x-upsert", "true");
            request.Content = new ByteArrayContent(content);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Supabase Storage upload failed. Status={Status}, Path={Path}, Response={Response}",
                    (int)response.StatusCode,
                    objectPath,
                    responseBody);
                throw new InvalidOperationException($"Supabase Storage upload failed with status {(int)response.StatusCode}.");
            }

            return $"{_supabaseUrl}/storage/v1/object/public/{BucketName}/{objectPath}";
        }

        public string? TryExtractObjectPath(string? fileUrl)
        {
            if (string.IsNullOrWhiteSpace(fileUrl)) return null;
            var marker = $"/storage/v1/object/public/{BucketName}/";
            int idx = fileUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var path = fileUrl[(idx + marker.Length)..].TrimStart('/');
            return path.Length == 0 ? null : path;
        }

        public async Task<bool> DeleteAsync(string objectPath, CancellationToken cancellationToken = default)
        {
            var normalized = (objectPath ?? string.Empty).TrimStart('/');
            if (normalized.Length == 0) return false;

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Delete,
                    $"{_supabaseUrl}/storage/v1/object/{BucketName}/{normalized}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceRoleKey);
                request.Headers.Add("apikey", _serviceRoleKey);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning(
                        "Supabase Storage delete returned {Status} for {Path}",
                        (int)response.StatusCode,
                        normalized);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Supabase Storage delete failed for {Path}", normalized);
                return false;
            }
        }
    }
}
