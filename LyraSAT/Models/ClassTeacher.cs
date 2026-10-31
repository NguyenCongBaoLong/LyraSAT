using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LyraSAT.Models
{
    public class ClassTeacher
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ClassCode { get; set; } = string.Empty;

        [Required]
        public int TeacherId { get; set; }

        [ForeignKey("TeacherId")]
        public virtual User? Teacher { get; set; }

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    }
}
