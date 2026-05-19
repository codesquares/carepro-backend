using Application.DTOs;
using Application.Interfaces.Content;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructure.Content.Services
{
    public class ReceiptPdfService : IReceiptPdfService
    {
        static ReceiptPdfService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public byte[] GenerateCommitmentReceipt(CommitmentReceiptData data)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginHorizontal(50);
                    page.MarginVertical(45);
                    page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Black));

                    // Header
                    page.Header().Column(col =>
                    {
                        col.Item().AlignCenter().Text("CAREPRO")
                            .Bold().FontSize(22).FontColor(Colors.Blue.Darken2);
                        col.Item().AlignCenter().Text("Payment Receipt")
                            .FontSize(13).FontColor(Colors.Grey.Darken2);
                        col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(Colors.Blue.Darken2);
                    });

                    page.Content().PaddingTop(20).Column(col =>
                    {
                        // Receipt type banner
                        col.Item().Background(Colors.Blue.Lighten4).Padding(10).Column(banner =>
                        {
                            banner.Item().AlignCenter().Text("BOOKING COMMITMENT FEE RECEIPT")
                                .Bold().FontSize(12).FontColor(Colors.Blue.Darken3);
                        });

                        col.Item().PaddingTop(18).Column(details =>
                        {
                            // Transaction info
                            SectionHeader(details, "Transaction Details");
                            Row(details, "Transaction Reference", data.TransactionReference);
                            if (!string.IsNullOrEmpty(data.FlutterwaveTransactionId))
                                Row(details, "Flutterwave ID", data.FlutterwaveTransactionId);
                            Row(details, "Date & Time", data.PaidAt.ToString("dddd, MMMM dd, yyyy 'at' HH:mm") + " UTC");
                            Row(details, "Status", "PAID");

                            col.Item().PaddingTop(14);

                            // Parties
                            SectionHeader(details, "Parties");
                            Row(details, "Client", data.ClientName);
                            Row(details, "Client Email", data.ClientEmail);
                            Row(details, "Caregiver", data.CaregiverName);
                            Row(details, "Service / Gig", data.GigTitle);

                            col.Item().PaddingTop(14);

                            // Payment breakdown
                            SectionHeader(details, "Payment Breakdown");
                            Row(details, "Commitment Fee", FormatCurrency(data.CommitmentFee, data.Currency));
                            Row(details, "Gateway Fees", FormatCurrency(data.GatewayFees, data.Currency));
                            col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                            col.Item().PaddingVertical(4).Row(row =>
                            {
                                row.RelativeItem().Text("Total Charged").Bold().FontSize(11);
                                row.ConstantItem(160).AlignRight().Text(FormatCurrency(data.TotalCharged, data.Currency))
                                    .Bold().FontSize(11).FontColor(Colors.Green.Darken2);
                            });
                        });

                        col.Item().PaddingTop(24).Background(Colors.Grey.Lighten4).Padding(12).Column(note =>
                        {
                            note.Item().Text("What this payment unlocks:").Bold().FontSize(10);
                            note.Item().PaddingTop(4).Text(
                                "This booking commitment fee grants you direct messaging access with the caregiver " +
                                "to discuss care details before placing a full order. The ₦5,000 fee will be " +
                                "deducted from your total order payment when you proceed.")
                                .FontSize(9).FontColor(Colors.Grey.Darken2);
                        });
                    });

                    // Footer
                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.Span("CarePro Platform  •  ").FontSize(8).FontColor(Colors.Grey.Darken1);
                        t.Span("This is a computer-generated receipt and requires no signature.")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            }).GeneratePdf();
        }

        public byte[] GenerateOrderReceipt(OrderReceiptData data)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginHorizontal(50);
                    page.MarginVertical(45);
                    page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Black));

                    // Header
                    page.Header().Column(col =>
                    {
                        col.Item().AlignCenter().Text("CAREPRO")
                            .Bold().FontSize(22).FontColor(Colors.Blue.Darken2);
                        col.Item().AlignCenter().Text("Payment Receipt")
                            .FontSize(13).FontColor(Colors.Grey.Darken2);
                        col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(Colors.Blue.Darken2);
                    });

                    page.Content().PaddingTop(20).Column(col =>
                    {
                        // Receipt type banner
                        col.Item().Background(Colors.Green.Lighten4).Padding(10).Column(banner =>
                        {
                            banner.Item().AlignCenter().Text("CARE SERVICE ORDER PAYMENT RECEIPT")
                                .Bold().FontSize(12).FontColor(Colors.Green.Darken3);
                        });

                        col.Item().PaddingTop(18).Column(details =>
                        {
                            // Transaction info
                            SectionHeader(details, "Transaction Details");
                            Row(details, "Transaction Reference", data.TransactionReference);
                            if (!string.IsNullOrEmpty(data.FlutterwaveTransactionId))
                                Row(details, "Flutterwave ID", data.FlutterwaveTransactionId);
                            if (!string.IsNullOrEmpty(data.ClientOrderId))
                                Row(details, "Order ID", data.ClientOrderId);
                            Row(details, "Date & Time", data.PaidAt.ToString("dddd, MMMM dd, yyyy 'at' HH:mm") + " UTC");
                            Row(details, "Status", "PAID");

                            col.Item().PaddingTop(14);

                            // Parties
                            SectionHeader(details, "Parties");
                            Row(details, "Client", data.ClientName);
                            Row(details, "Client Email", data.ClientEmail);
                            Row(details, "Caregiver", data.CaregiverName);
                            Row(details, "Service / Gig", data.GigTitle);

                            col.Item().PaddingTop(14);

                            // Service info
                            SectionHeader(details, "Service Details");
                            Row(details, "Service Type", Capitalize(data.ServiceType));
                            if (data.FrequencyPerWeek > 0)
                                Row(details, "Frequency", $"{data.FrequencyPerWeek}x per week");

                            col.Item().PaddingTop(14);

                            // Payment breakdown
                            SectionHeader(details, "Payment Breakdown");
                            Row(details, "Base Price", FormatCurrency(data.BasePrice, data.Currency));
                            if (data.OrderFee > 0)
                                Row(details, "Order Fee", FormatCurrency(data.OrderFee, data.Currency));
                            if (data.ServiceCharge > 0)
                                Row(details, "Service Charge", FormatCurrency(data.ServiceCharge, data.Currency));
                            if (data.GatewayFees > 0)
                                Row(details, "Gateway Fees", FormatCurrency(data.GatewayFees, data.Currency));
                            if (data.CommitmentFeeDeducted > 0)
                                Row(details, "Commitment Fee Deducted", $"- {FormatCurrency(data.CommitmentFeeDeducted, data.Currency)}");
                            col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                            col.Item().PaddingVertical(4).Row(row =>
                            {
                                row.RelativeItem().Text("Total Charged").Bold().FontSize(11);
                                row.ConstantItem(160).AlignRight().Text(FormatCurrency(data.TotalCharged, data.Currency))
                                    .Bold().FontSize(11).FontColor(Colors.Green.Darken2);
                            });
                        });
                    });

                    // Footer
                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.Span("CarePro Platform  •  ").FontSize(8).FontColor(Colors.Grey.Darken1);
                        t.Span("This is a computer-generated receipt and requires no signature.")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            }).GeneratePdf();
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static void SectionHeader(ColumnDescriptor col, string title)
        {
            col.Item().Background(Colors.Grey.Lighten3).PaddingHorizontal(6).PaddingVertical(4)
                .Text(title).Bold().FontSize(10).FontColor(Colors.Blue.Darken3);
            col.Item().PaddingBottom(2);
        }

        private static void Row(ColumnDescriptor col, string label, string value)
        {
            col.Item().PaddingVertical(2).Row(row =>
            {
                row.ConstantItem(175).Text(label).FontColor(Colors.Grey.Darken2);
                row.RelativeItem().Text(value).Bold();
            });
        }

        private static string FormatCurrency(decimal amount, string currency) =>
            $"{currency} {amount:N2}";

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
    }
}
