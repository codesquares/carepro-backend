using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Assessment
    {
        public ObjectId Id { get; set; }
        public string UserId { get; set; }
        public string CaregiverId { get; set; }
        public string UserType { get; set; } // "Cleaner" or "Caregiver"
        public DateTime StartTimestamp { get; set; }
        public DateTime EndTimestamp { get; set; }
        public int Score { get; set; } // Percentage score (0-100)
        public bool Passed { get; set; } // Based on 70% threshold
        public List<AssessmentQuestion> Questions { get; set; }
        public string Status { get; set; }
        public DateTime AssessedDate { get; set; }
        public DateTime UpdatedAt { get; set; }
    }


    public class AssessmentQuestion
    {
        public string QuestionId { get; set; } // Reference to QuestionBank
        public string Question { get; set; } // Question text (snapshot for audit)
        public List<string> Options { get; set; } // Array of options (snapshot for audit)
        public string CorrectAnswer { get; set; } // The correct answer (snapshot for audit)
        public string UserAnswer { get; set; } // User's selected answer
        public bool IsCorrect { get; set; } // Boolean indicator
    }
}
