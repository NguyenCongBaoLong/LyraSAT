namespace LyraSAT.DTOs
{
    public class AddTeacherRequest
    {
        public int TeacherId { get; set; }
        public int RequestUserId { get; set; }
    }

    public class RemoveTeacherRequest
    {
        public int TeacherId { get; set; }
        public int RequestUserId { get; set; }
    }

    public class TransferKeyRequest
    {
        public int NewLeadTeacherId { get; set; }
        public int RequestUserId { get; set; }
    }
}
