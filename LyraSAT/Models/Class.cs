using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LyraSAT.Models
{
    public class Class
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ClassCode { get; set; } = string.Empty; // "SAT01"

        [Required]
        [StringLength(250)]
        public string ClassName { get; set; } = string.Empty; // "Lớp Digital SAT Căn Bản"

        public int? CreatedByAdminId { get; set; }
        public int? TeacherId { get; set; }

        [ForeignKey("TeacherId")]
        public virtual User? Teacher { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<StudentClass>? StudentClasses { get; set; }
    }
}
