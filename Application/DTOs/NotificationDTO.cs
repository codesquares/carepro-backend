using Domain.Entities;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class NotificationDTO
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string Type { get; set; }
        public string Content { get; set; }
        public string Title { get; set; }
        public bool IsRead { get; set; }
        public string? RelatedEntityId { get; set; }
        //  public string? ReferenceType { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateNotificationRequest
    {
        public string UserId { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string Type { get; set; }
        public string? ReferenceId { get; set; }
        public string? ReferenceType { get; set; }
    }

    public class AddNotificationRequest
    {
        //  public ObjectId Id { get; set; }
        public string RecipientId { get; set; } // User receiving the notification
        public string SenderId { get; set; } // User who triggered the notification (optional)
        public string Type { get; set; } // Message, Payment, etc.
        public string Content { get; set; } // Notification text
        public string? Title { get; set; } // Notification title
                                           // public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
                                           // public bool IsRead { get; set; } = false;
        public string RelatedEntityId { get; set; } // ID of message/payment/gig




    }

    public class NotificationResponse
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string UserFullName { get; set; }
        public string? SenderId { get; set; }
        public string Type { get; set; }
        public string Content { get; set; }
        public string Title { get; set; }
        public bool IsRead { get; set; }
        public string? RelatedEntityId { get; set; }
        public string? OrderId { get; set; }
        public string? RelatedEntityType { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Single source of truth for all notification type strings.
    /// All values use consistent snake_case for frontend compatibility.
    /// Frontend should use these exact strings in their notification routing switch.
    /// </summary>
    public static class NotificationTypes
    {
        // ── Chat ──
        public const string ChatMessage = "chat_message";

        // ── Gigs ──
        public const string NewGig = "new_gig";

        // ── Orders ──
        public const string OrderReceived = "order_received";
        public const string OrderConfirmation = "order_confirmation";
        public const string OrderCompleted = "order_completed";
        public const string OrderCancelled = "order_cancelled";
        public const string OrderDisputed = "order_disputed";

        // ── Disputes ──
        public const string DisputeRaised = "dispute_raised";
        public const string DisputeUnderReview = "dispute_under_review";
        public const string DisputeResolved = "dispute_resolved";
        public const string DisputeDismissed = "dispute_dismissed";
        public const string VisitApproved = "visit_approved";
        public const string VisitDisputed = "visit_disputed";
        public const string VisitSubmitted = "visit_submitted";

        // ── Booking ──
        public const string BookingConfirmed = "booking_confirmed";
        public const string CommitmentConfirmed = "commitment_confirmed";

        // ── Contract lifecycle (caregiver-side) ──
        public const string ContractReceived = "contract_received";
        public const string ContractPending = "contract_pending";
        public const string ContractAccepted = "contract_accepted";
        public const string ContractRejected = "contract_rejected";
        public const string ContractResponse = "contract_response";
        public const string ContractReviewRequested = "contract_review_requested";
        public const string ContractReminder = "contract_reminder";
        public const string ContractExpired = "contract_expired";

        // ── Contract lifecycle (client-side) ──
        public const string ContractPendingApproval = "contract_pending_approval";
        public const string ContractPendingClientApproval = "contract_pending_client_approval";
        public const string ContractRevised = "contract_revised";
        public const string ContractClientApproved = "contract_client_approved";
        public const string ContractClientRejected = "contract_client_rejected";
        public const string ContractClientResponse = "contract_client_response";
        public const string ContractClientReminder = "contract_client_reminder";

        // ── Certificates ──
        public const string CertificateUploaded = "certificate_uploaded";
        public const string CertificateVerification = "certificate_verification";
        public const string CertificateManualApproval = "certificate_manual_approval";
        public const string CertificateManualRejection = "certificate_manual_rejection";
        public const string CertificateReview = "certificate_review";

        // ── Subscriptions ──
        public const string SubscriptionCreated = "subscription_created";
        public const string SubscriptionCancellationScheduled = "subscription_cancellation_scheduled";
        public const string SubscriptionCancellationNotice = "subscription_cancellation_notice";
        public const string SubscriptionReactivated = "subscription_reactivated";
        public const string SubscriptionTerminated = "subscription_terminated";
        public const string SubscriptionPlanChanged = "subscription_plan_changed";
        public const string SubscriptionPaused = "subscription_paused";
        public const string SubscriptionResumed = "subscription_resumed";
        public const string SubscriptionSuspended = "subscription_suspended";
        public const string SubscriptionCancelled = "subscription_cancelled";
        public const string SubscriptionEnded = "subscription_ended";

        // ── Payments ──
        public const string PaymentReceived = "payment_received";
        public const string PaymentConfirmed = "payment_confirmed";
        public const string PaymentMethodUpdated = "payment_method_updated";
        public const string RecurringPaymentSuccessful = "recurring_payment_successful";
        public const string PaymentFailed = "payment_failed";
        public const string EarningsAdded = "earnings_added";
        public const string OrderPayment = "order_payment";
        public const string RefundProcessed = "refund_processed";

        // ── Withdrawals ──
        public const string WithdrawalRequest = "withdrawal_request";
        public const string WithdrawalVerified = "withdrawal_verified";
        public const string WithdrawalCompleted = "withdrawal_completed";
        public const string WithdrawalRejected = "withdrawal_rejected";

        // ── Reviews ──
        public const string NewReview = "new_review";

        // ── System ──
        public const string SystemNotice = "system_notice";
        public const string SystemAlert = "system_alert";

        // ── Care Request Matching ──
        public const string CareRequestMatched = "care_request_matched";
        public const string CareRequestNoMatch = "care_request_no_match";
        public const string CareRequestAdminMatchUpdate = "care_request_admin_match_update";
        public const string CareRequestAdminNoMatch = "care_request_admin_no_match";

        // ── Visit Task Proposals ──
        public const string TaskProposedByClient = "task_proposed_by_client";
        public const string TaskProposalAccepted = "task_proposal_accepted";
        public const string TaskProposalRejected = "task_proposal_rejected";

        // ── Contract Task Proposals ──
        public const string ContractTaskProposedByClient = "contract_task_proposed_by_client";
        public const string ContractTaskProposalAccepted = "contract_task_proposal_accepted";
        public const string ContractTaskProposalRejected = "contract_task_proposal_rejected";

        // ── Gig Deletion ──
        public const string GigDeletionReminder = "gig_deletion_reminder";
        public const string GigPermanentlyDeleted = "gig_permanently_deleted";

        // ── Broadcast ──
        public const string Broadcast = "broadcast";
    }

    public class BroadcastNotificationRequest
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public string? Type { get; set; } // defaults to "broadcast"
    }

    public class BroadcastNotificationResponse
    {
        public bool Success { get; set; }
        public int RecipientsCount { get; set; }
        public string Message { get; set; }
    }
}
