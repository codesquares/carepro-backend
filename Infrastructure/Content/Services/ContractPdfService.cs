using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructure.Content.Services
{
    public class ContractPdfService : IContractPdfService
    {
        static ContractPdfService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public byte[] GeneratePdf(ContractGenerationDataDTO data)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginHorizontal(40);
                    page.MarginVertical(35);
                    page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Black));

                    page.Header().Column(col =>
                    {
                        col.Item().AlignCenter().Text("CAREPRO CARE SERVICE AGREEMENT")
                            .Bold().FontSize(16).FontColor(Colors.Blue.Darken2);
                        col.Item().AlignCenter().Text("Facilitated through the CarePro Platform")
                            .FontSize(9).FontColor(Colors.Grey.Darken1);
                        col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Blue.Darken2);
                    });

                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        // Meta
                        col.Item().PaddingBottom(8).Column(meta =>
                        {
                            meta.Item().Text(t =>
                            {
                                t.Span("Contract Reference: ").Bold();
                                t.Span(data.ContractId);
                            });
                            meta.Item().Text(t =>
                            {
                                t.Span("Order Reference: ").Bold();
                                t.Span(data.OrderId);
                            });
                            meta.Item().Text(t =>
                            {
                                t.Span("Date: ").Bold();
                                t.Span(data.GeneratedAt.ToString("dddd, MMMM dd, yyyy 'at' HH:mm") + " UTC");
                            });
                        });

                        // Section 1: Parties
                        SectionHeader(col, "Section 1: Parties to This Agreement");
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1.2f);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                            });

                            HeaderCell(table, "");
                            HeaderCell(table, "Client (Care Recipient)");
                            HeaderCell(table, "Caregiver (Service Provider)");

                            BodyCell(table, "Full Name", true);
                            BodyCell(table, data.ClientFullName);
                            BodyCell(table, data.CaregiverFullName);

                            BodyCell(table, "ID", true);
                            BodyCell(table, data.ClientId);
                            BodyCell(table, data.CaregiverId);

                            BodyCell(table, "Phone", true);
                            BodyCell(table, data.ClientPhone ?? "On file");
                            BodyCell(table, data.CaregiverPhone ?? "On file");

                            if (!string.IsNullOrWhiteSpace(data.CaregiverQualifications))
                            {
                                BodyCell(table, "Qualifications", true);
                                BodyCell(table, "—");
                                BodyCell(table, data.CaregiverQualifications);
                            }
                        });

                        // Section 2: Platform Relationship
                        SectionHeader(col, "Section 2: Platform Relationship");
                        col.Item().Text("CarePro is a technology platform that connects clients seeking care services with independent caregivers. " +
                            "Caregivers are independent contractors and market participants offering their services on the CarePro platform. " +
                            "CarePro does not employ caregivers and is not a party to the care relationship between client and caregiver. " +
                            "CarePro facilitates the matching, payment processing, and contract management only.").FontSize(10);

                        // Section 3: Service Details
                        SectionHeader(col, "Section 3: Service Details");
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1.5f);
                                columns.RelativeColumn(3);
                            });

                            BodyCell(table, "Service", true);
                            BodyCell(table, data.GigTitle);

                            if (!string.IsNullOrWhiteSpace(data.GigDescription))
                            {
                                BodyCell(table, "Description", true);
                                BodyCell(table, data.GigDescription);
                            }

                            if (!string.IsNullOrWhiteSpace(data.GigCategory))
                            {
                                BodyCell(table, "Category", true);
                                BodyCell(table, data.GigCategory);
                            }

                            BodyCell(table, "Package", true);
                            BodyCell(table, data.Package.PackageType?.Replace("_", " ") ?? "Standard");

                            BodyCell(table, "Visits Per Week", true);
                            BodyCell(table, data.Package.VisitsPerWeek.ToString());

                            BodyCell(table, "Hours Per Visit", true);
                            BodyCell(table, "4–6 hours");

                            BodyCell(table, "Duration", true);
                            BodyCell(table, $"{data.Package.DurationWeeks} weeks");
                        });

                        // Section 4: Contract Period
                        SectionHeader(col, "Section 4: Contract Period");
                        col.Item().Text(t =>
                        {
                            t.Span("Start Date: ").Bold();
                            t.Span(data.ContractStartDate.ToString("dddd, MMMM dd, yyyy"));
                        });
                        col.Item().Text(t =>
                        {
                            t.Span("End Date: ").Bold();
                            t.Span(data.ContractEndDate.ToString("dddd, MMMM dd, yyyy"));
                        });
                        col.Item().PaddingTop(4).Text("This contract is binding for as long as the associated order is not terminated or cancelled by either party or by CarePro.").FontSize(10);

                        // Section 5: Agreed Weekly Schedule
                        SectionHeader(col, "Section 5: Agreed Weekly Schedule");
                        if (data.Schedule.Any())
                        {
                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(1.5f);
                                    columns.RelativeColumn(1.5f);
                                });

                                HeaderCell(table, "Day");
                                HeaderCell(table, "Start Time");
                                HeaderCell(table, "End Time");

                                foreach (var slot in data.Schedule)
                                {
                                    BodyCell(table, slot.DayOfWeek.ToString());
                                    BodyCell(table, slot.StartTime);
                                    BodyCell(table, slot.EndTime);
                                }
                            });
                        }
                        else
                        {
                            col.Item().Text("Schedule to be confirmed by both parties.");
                        }

                        // Section 6: Service Location
                        SectionHeader(col, "Section 6: Service Location");
                        var address = data.ServiceAddress;
                        if (!string.IsNullOrWhiteSpace(data.City)) address += $", {data.City}";
                        if (!string.IsNullOrWhiteSpace(data.State)) address += $", {data.State}";
                        col.Item().Text(t =>
                        {
                            t.Span("Address: ").Bold();
                            t.Span(address);
                        });
                        if (!string.IsNullOrWhiteSpace(data.AccessInstructions))
                        {
                            col.Item().Text(t =>
                            {
                                t.Span("Access Instructions: ").Bold();
                                t.Span(data.AccessInstructions);
                            });
                        }

                        // Section 7: Care Responsibilities
                        SectionHeader(col, "Section 7: Care Responsibilities");
                        col.Item().Text("7.1 Primary Care Tasks").Bold().FontSize(10);
                        if (data.Tasks.Any())
                        {
                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(3);
                                    columns.RelativeColumn(1);
                                });

                                HeaderCell(table, "Task");
                                HeaderCell(table, "Description");
                                HeaderCell(table, "Priority");

                                foreach (var task in data.Tasks)
                                {
                                    BodyCell(table, task.Title);
                                    BodyCell(table, task.Description);
                                    BodyCell(table, task.Priority.ToString());
                                }
                            });
                        }
                        else
                        {
                            col.Item().Text("General care services as discussed and agreed upon by both parties.");
                        }

                        if (!string.IsNullOrWhiteSpace(data.SpecialClientRequirements))
                        {
                            col.Item().PaddingTop(4).Text("7.2 Special Client Requirements").Bold().FontSize(10);
                            col.Item().Text(data.SpecialClientRequirements);
                        }
                        if (!string.IsNullOrWhiteSpace(data.CaregiverNotes))
                        {
                            col.Item().PaddingTop(4).Text("7.3 Caregiver Notes").Bold().FontSize(10);
                            col.Item().Text(data.CaregiverNotes);
                        }

                        col.Item().PaddingTop(4).Text("7.4 Service Standards").Bold().FontSize(10);
                        BulletList(col,
                            "All services shall be provided with professionalism, compassion, and care.",
                            "The Caregiver shall follow established care protocols and best practices.",
                            "Regular communication with the Client and/or family members as appropriate.",
                            "Maintain accurate records of care provided through the CarePro platform.");

                        // Section 8: Cancellation policy
                        SectionHeader(col, "Section 8: Visit Cancellation & Rescheduling Policy");
                        col.Item().Text("8.1 Cancellation by Client").Bold().FontSize(10);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                            });

                            HeaderCell(table, "Notice Period");
                            HeaderCell(table, "Client Receives");
                            HeaderCell(table, "Caregiver Receives");

                            BodyCell(table, "24+ hours before visit");
                            BodyCell(table, "100% of visit fee to wallet");
                            BodyCell(table, "0%");

                            BodyCell(table, "12–24 hours before visit");
                            BodyCell(table, "50% of visit fee to wallet");
                            BodyCell(table, "50% of visit fee");

                            BodyCell(table, "Less than 12 hours");
                            BodyCell(table, "0% — fee forfeited");
                            BodyCell(table, "100% of visit fee");
                        });

                        col.Item().PaddingTop(4).Text("8.2 Cancellation by Caregiver").Bold().FontSize(10);
                        BulletList(col,
                            "The Caregiver must contact the Client at least 24 hours before a scheduled visit if they need to cancel, so the Client can cancel the visit through the platform.",
                            "If the Caregiver fails to show without 24-hour notice, the matter will be reviewed by CarePro and may result in penalties, suspension, or contract termination.");

                        col.Item().PaddingTop(4).Text("8.3 Rescheduling").Bold().FontSize(10);
                        BulletList(col,
                            "Either party may request schedule changes with 48-hour advance notice.",
                            "Permanent schedule changes require mutual agreement via the CarePro platform.",
                            "Emergency changes should be communicated immediately through the platform messaging system.");

                        // Section 9: Reporting
                        SectionHeader(col, "Section 9: Reporting & Documentation");
                        col.Item().Text("9.1 Observation Reports").Bold().FontSize(10);
                        BulletList(col,
                            "The Caregiver is required to submit an observation report after each visit documenting the care provided, client condition, and any notable observations.",
                            "Observation reports protect both the Caregiver and Client in case of any disputes or allegations.");

                        col.Item().PaddingTop(4).Text("9.2 Incident Reports").Bold().FontSize(10);
                        BulletList(col,
                            "The Caregiver must submit an incident report immediately through the CarePro platform for any accidents, injuries, unusual events, or safety concerns that occur during a visit.",
                            "Failure to report incidents may result in liability for the Caregiver.");

                        // Section 10: Disputes
                        SectionHeader(col, "Section 10: Disputes");
                        BulletList(col,
                            "The Client may dispute a visit if not satisfied with the care provided. All disputes are subject to investigation by CarePro.",
                            "CarePro will review observation reports, incident reports, and any other evidence before making a determination.",
                            "Both parties agree to cooperate fully with CarePro's dispute resolution process.");

                        // Section 11: Safety & Confidentiality
                        SectionHeader(col, "Section 11: Safety & Confidentiality");
                        col.Item().Text("11.1 Safety").Bold().FontSize(10);
                        BulletList(col,
                            "The Caregiver has been verified through CarePro's onboarding and background check process.",
                            "Both parties must maintain a safe environment for care delivery.",
                            "Emergency situations should be handled by contacting emergency services first, then reporting through CarePro.");

                        col.Item().PaddingTop(4).Text("11.2 Confidentiality").Bold().FontSize(10);
                        BulletList(col,
                            "All personal, medical, and household information shared during this care arrangement is strictly confidential.",
                            "Neither party may share the other's personal information outside the care relationship.",
                            "Confidentiality obligations survive termination of this Agreement.");

                        // Section 12: Termination
                        SectionHeader(col, "Section 12: Termination");
                        col.Item().Text("12.1 By Either Party").Bold().FontSize(10);
                        BulletList(col,
                            "Either party may terminate this contract through the CarePro platform.",
                            "The contract remains binding until formally terminated or until the order is cancelled.");

                        col.Item().PaddingTop(4).Text("12.2 Immediate Termination (For Cause)").Bold().FontSize(10);
                        col.Item().Text("CarePro reserves the right to immediately terminate this contract for:");
                        BulletList(col,
                            "Safety protocol breach",
                            "Unprofessional conduct or abuse",
                            "Confidentiality violation",
                            "Repeated failure to provide agreed services",
                            "Fraud or misrepresentation");

                        // Section 13: Limitation of Liability
                        SectionHeader(col, "Section 13: Limitation of Liability");
                        BulletList(col,
                            "CarePro operates as a marketplace platform and is not liable for the actions or omissions of either party.",
                            "The Caregiver operates as an independent contractor and is solely responsible for the quality of care provided.");

                        // Section 14: Acknowledgment
                        SectionHeader(col, "Section 14: Acknowledgment");
                        col.Item().Text("Both parties acknowledge they have read, understood, and agree to all terms of this Care Service Agreement.");

                        // Signature block
                        col.Item().PaddingTop(30).Row(row =>
                        {
                            row.RelativeItem().Column(sig =>
                            {
                                sig.Item().LineHorizontal(0.5f);
                                sig.Item().PaddingTop(2).Text(t =>
                                {
                                    t.Span("Client: ").Bold();
                                    t.Span(data.ClientFullName);
                                });
                                sig.Item().Text($"Date: {data.GeneratedAt:MMMM dd, yyyy}").FontSize(9);
                            });

                            row.ConstantItem(40); // spacer

                            row.RelativeItem().Column(sig =>
                            {
                                sig.Item().LineHorizontal(0.5f);
                                sig.Item().PaddingTop(2).Text(t =>
                                {
                                    t.Span("Caregiver: ").Bold();
                                    t.Span(data.CaregiverFullName);
                                });
                                sig.Item().Text($"Date: {data.GeneratedAt:MMMM dd, yyyy}").FontSize(9);
                            });
                        });
                    });

                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.Span("CarePro Care Service Agreement — Page ").FontSize(8).FontColor(Colors.Grey.Darken1);
                        t.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
                        t.Span(" of ").FontSize(8).FontColor(Colors.Grey.Darken1);
                        t.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            });

            return document.GeneratePdf();
        }

        private static void SectionHeader(ColumnDescriptor col, string title)
        {
            col.Item().PaddingTop(14).PaddingBottom(4).Column(header =>
            {
                header.Item().Text(title).Bold().FontSize(12).FontColor(Colors.Blue.Darken2);
                header.Item().LineHorizontal(0.5f).LineColor(Colors.Blue.Darken2);
            });
        }

        private static void HeaderCell(TableDescriptor table, string text)
        {
            table.Cell().Background(Colors.Blue.Lighten4).Padding(5)
                .Text(text).Bold().FontSize(9);
        }

        private static void BodyCell(TableDescriptor table, string text, bool bold = false)
        {
            var cell = table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
            if (bold)
                cell.Text(text).Bold().FontSize(9);
            else
                cell.Text(text).FontSize(9);
        }

        private static void BulletList(ColumnDescriptor col, params string[] items)
        {
            foreach (var item in items)
            {
                col.Item().PaddingLeft(12).Row(row =>
                {
                    row.ConstantItem(10).Text("•").FontSize(10);
                    row.RelativeItem().Text(item).FontSize(10);
                });
            }
        }
    }
}
