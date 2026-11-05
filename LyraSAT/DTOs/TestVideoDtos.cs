namespace LyraSAT.DTOs
{
    // Gắn một video chữa bài mới cho đề đã giao.
    public class CreateTestVideoDto
    {
        // Có thể để trống, khi đó client tự đặt tên "Video 1", "Video 2"...
        public string? Title { get; set; }
        public string Url { get; set; } = string.Empty;
    }

    // Một video trả về cho client (học sinh / giáo viên xem lại bài).
    public class TestVideoItemDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}
