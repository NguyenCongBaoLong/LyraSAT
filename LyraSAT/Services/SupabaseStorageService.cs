using System.Net.Http.Headers;

namespace LyraSAT.Services
{
    public interface ISupabaseStorageService
    {
        Task<string> UploadAsync(byte[] content, string objectPath, string contentType, CancellationToken cancellationToken = default);
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
            _supabaseUrl = configuration["Supabase:Url"]?.TrimEnd('/')
                ?? throw new InvalidOperationException("Supabase:Url is not configured.");
            _serviceRoleKey = configuration["Supabase:ServiceRoleKey"]
                ?? throw new InvalidOperationException("Supabase:ServiceRoleKey is not configured.");
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
    }
}
