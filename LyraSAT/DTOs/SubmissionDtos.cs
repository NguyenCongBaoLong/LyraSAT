namespace LyraSAT.DTOs
{
    public class SubmitDto
    {
        public int StudentId { get; set; }
        public int TestId { get; set; }
        public int TimeSpent { get; set; } // Tính bằng giây
        public Dictionary<int, string> Answers { get; set; } = new();
    }
}
