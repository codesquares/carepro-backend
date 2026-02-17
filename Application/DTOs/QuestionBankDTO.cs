using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    public class QuestionBankDTO
    {
        public string Id { get; set; }
        public string Category { get; set; }
        public string? ServiceCategory { get; set; }
        public string UserType { get; set; } // "Cleaner", "Caregiver", or "Both"
        public string QuestionType { get; set; } = "MultipleChoice";
        public string DifficultyLevel { get; set; } = "Medium";
        public string Question { get; set; }
        public List<string> Options { get; set; } // A, B, C, D options
        public string CorrectAnswer { get; set; } // A, B, C, D
        public string Explanation { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool Active { get; set; }
    }

    /// <summary>
    /// DTO for returning questions to assessment takers — CorrectAnswer is STRIPPED.
    /// </summary>
    public class AssessmentQuestionBankDTO
    {
        public string Id { get; set; }
        public string Category { get; set; }
        public string? ServiceCategory { get; set; }
        public string QuestionType { get; set; }
        public string DifficultyLevel { get; set; }
        public string Question { get; set; }
        public List<string> Options { get; set; }
        // CorrectAnswer intentionally omitted — never send to frontend during assessment
    }

    public class AddQuestionBankRequest
    {
        [Required]
        public string Category { get; set; }

        /// <summary>
        /// Null for general questions.
        /// For specialized: "MedicalSupport", "PostSurgeryCare", "SpecialNeedsCare", "Palliative", "TherapyAndWellness"
        /// </summary>
        public string? ServiceCategory { get; set; }

        [Required]
        public string UserType { get; set; } // "Cleaner", "Caregiver", or "Both"

        public string QuestionType { get; set; } = "MultipleChoice";
        public string DifficultyLevel { get; set; } = "Medium";

        [Required]
        public string Question { get; set; }

        [Required]
        [MinLength(4)]
        [MaxLength(4)]
        public List<string> Options { get; set; } // A, B, C, D options

        [Required]
        public string CorrectAnswer { get; set; } // A, B, C, D

        public string Explanation { get; set; }
    }

    public class UpdateQuestionBankRequest
    {
        [Required]
        public string Id { get; set; }

        public string Category { get; set; }
        public string? ServiceCategory { get; set; }
        public string UserType { get; set; }
        public string QuestionType { get; set; }
        public string DifficultyLevel { get; set; }
        public string Question { get; set; }
        public List<string> Options { get; set; }
        public string CorrectAnswer { get; set; }
        public string Explanation { get; set; }
        public bool? Active { get; set; }
    }

    public class BatchAddQuestionBankRequest
    {
        [Required]
        public List<AddQuestionBankRequest> Questions { get; set; }
    }
}
