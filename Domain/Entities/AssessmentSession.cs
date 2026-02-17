using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    /// <summary>
    /// Tracks an active assessment session to bind fetched questions to a submission.
    /// Prevents question farming and ensures submitted answers match the assigned questions.
    /// </summary>
    public class AssessmentSession
    {
        public ObjectId Id { get; set; }

        /// <summary>
        /// The caregiver taking the assessment.
        /// </summary>
        public string CaregiverId { get; set; } = string.Empty;

        /// <summary>
        /// The service category for this assessment session.
        /// </summary>
        public string ServiceCategory { get; set; } = string.Empty;

        /// <summary>
        /// The IDs of the questions assigned to this session.
        /// </summary>
        public List<string> QuestionIds { get; set; } = new();

        /// <summary>
        /// When the session was created.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// When the session expires. After this time, the session cannot be submitted.
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Session status: "Active", "Submitted", "Expired"
        /// </summary>
        public string Status { get; set; } = "Active";
    }
}
