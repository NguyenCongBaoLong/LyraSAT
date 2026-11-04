namespace LyraSAT.DTOs
{
    // Gắn / gỡ link video chữa bài cho một đề đã giao.
    // Dùng sau khi đề đã được upload, giáo viên không phải tạo lại đề.
    public class UpdateVideoExplanationDto
    {
        // Chuỗi rỗng hoặc null = gỡ video khỏi đề.
        public string? VideoExplanationUrl { get; set; }
    }
}
