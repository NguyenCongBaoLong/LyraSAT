using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LyraSAT.Models
{
    public class Question
    {
        [Key]
        public int Id { get; set; }
        public int TestId { get; set; }

        [ForeignKey("TestId")]
        public Test Test { get; set; }
        public string? PassageText { get; set; }
        public string QuestionText { get; set; }
        public string OptionA { get; set; }
        public string OptionB { get; set; }
        public string OptionC { get; set; }
        public string OptionD { get; set; }
        public string CorrectAnswer { get; set; }
        // "MultipleChoice" (A/B/C/D) hoặc "FreeResponse" (học sinh gõ đáp án, chấm theo giá trị số).
        public string QuestionType { get; set; } = "MultipleChoice";
        // Các cách ghi được chấp nhận của câu tự luận, phân tách bằng dấu | ("3/2 | 1.5 | 1,5").
        public string? AcceptedAnswers { get; set; }
        public string? VideoExplanationUrl { get; set; }
        public string? Explanation { get; set; }        
        public string? Section { get; set; }            
        public int? ModuleNumber { get; set; }
        public ICollection<QuestionAsset> Assets { get; set; } = new List<QuestionAsset>();
    }
}
