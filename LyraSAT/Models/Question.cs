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
        public string? VideoExplanationUrl { get; set; }
        public string? Explanation { get; set; }        
        public string? Section { get; set; }            
        public int? ModuleNumber { get; set; }
    }
}
