using Domain.Entities;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class AssessmentDTO
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string CaregiverId { get; set; }
        public string UserType { get; set; } // "Cleaner" or "Caregiver"
        public DateTime StartTimestamp { get; set; }
        public DateTime EndTimestamp { get; set; }
        public int Score { get; set; }
        public bool Passed { get; set; }
        public List<AssessmentQuestion> Questions { get; set; }
        public string Status { get; set; }
        public DateTime AssessedDate { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class AssessmentQuestionDTO
    {
        public string QuestionId { get; set; }
        public string Question { get; set; }
        public List<string> Options { get; set; }
        public string CorrectAnswer { get; set; }
        public string UserAnswer { get; set; }
        public bool IsCorrect { get; set; }
    }

    public class AddAssessmentRequest
    {
        public string UserId { get; set; }
        public string CaregiverId { get; set; }
        public string UserType { get; set; } // "Cleaner" or "Caregiver"
        public List<AssessmentQuestion> Questions { get; set; }
        public string Status { get; set; }
        public int Score { get; set; }
    }

    public class AssessmentQuestionSubmitDTO
    {

        public string QuestionId { get; set; }
        public string UserAnswer { get; set; }
    }

    public class GetAssessmentQuestionsRequest
    {
        public string UserType { get; set; } // "Cleaner" or "Caregiver"
    }
}
