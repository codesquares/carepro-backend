using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class QuestionBank
    {
        public ObjectId Id { get; set; }
        public string Category { get; set; }

        /// <summary>
        /// Null or empty for general questions.
        /// For specialized: "MedicalSupport", "PostSurgeryCare", "SpecialNeedsCare", "Palliative", "TherapyAndWellness"
        /// </summary>
        public string? ServiceCategory { get; set; }

        public string UserType { get; set; } // "Cleaner", "Caregiver", or "Both"

        /// <summary>
        /// "MultipleChoice" or "ScenarioBased"
        /// </summary>
        public string QuestionType { get; set; } = "MultipleChoice";

        /// <summary>
        /// "Easy", "Medium", "Hard"
        /// </summary>
        public string DifficultyLevel { get; set; } = "Medium";

        public string Question { get; set; }
        public List<string> Options { get; set; } // A, B, C, D options
        public string CorrectAnswer { get; set; } // A, B, C, D
        public string Explanation { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool Active { get; set; }
    }
}
