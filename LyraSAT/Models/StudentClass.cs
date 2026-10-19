using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LyraSAT.Models
{
    public class StudentClass
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int ClassId { get; set; }

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public virtual User? User { get; set; }

        [ForeignKey("ClassId")]
        public virtual Class? Class { get; set; }
    }
}
