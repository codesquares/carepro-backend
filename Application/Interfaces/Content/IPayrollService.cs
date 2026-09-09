using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Admin CRUD + wallet-credit trigger for the <see cref="Domain.Entities.Payroll"/>
    /// table (Phase 9.7). Mirrors <see cref="IPackageService"/>'s admin-CRUD shape where
    /// it applies; Approve/MarkPaid are the status-transition operations specific to payroll.
    /// </summary>
    public interface IPayrollService
    {
        Task<PayrollDTO> CreatePayrollAsync(CreatePayrollRequest request);
        Task<PayrollDTO?> GetPayrollByIdAsync(string id);
        Task<List<PayrollDTO>> GetAllPayrollsAsync(string? caregiverId = null);

        /// <summary>
        /// Approves a Draft payroll record and credits the caregiver's wallet directly to
        /// WithdrawableBalance via ICaregiverWalletService.CreditRecurringPaymentAsync —
        /// approval is itself the finality/release event, unlike order-based earnings
        /// which need a separate per-visit release.
        /// </summary>
        Task<PayrollDTO> ApprovePayrollAsync(string id, ApprovePayrollRequest request, string adminId, string adminEmail);

        /// <summary>Marks an Approved payroll record Paid — a reconciliation status only;
        /// the wallet was already credited at approval.</summary>
        Task<bool> MarkPayrollPaidAsync(string id);

        /// <summary>Deletes a Draft payroll record. Approved/Paid records cannot be deleted
        /// since a wallet credit has already been made against them.</summary>
        Task<bool> DeletePayrollAsync(string id);
    }
}
