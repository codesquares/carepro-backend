using Application.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Infrastructure.Content.Helpers
{
    public static class VettingNoticeTypes
    {
        public const string Initial = "initial";
        public const string Reminder7d = "reminder_7d";
        public const string Reminder1d = "reminder_1d";
        public static readonly string[] All = { Initial, Reminder7d, Reminder1d };
        public static bool IsValid(string? t) => t != null && All.Contains(t);
    }

    public static class VettingSteps
    {
        public const string Classification = "classification";
        public const string Guarantors = "guarantors";
        public const string AddressHistory = "address-history";
        public const string PoliceClearance = "police-clearance";
    }

    public record VettingNoticeMessage(string Subject, string Html, string Text, string InAppTitle, string InAppContent);

    /// <summary>
    /// Renders the vetting enforcement notice. Plain language, only the steps the caregiver is missing, deadline in Africa/Lagos
    /// (shared LagosTime helper), real line breaks in the text body and real markup in the HTML (never literal "\n").
    /// No marketing, no promotional links, no unsubscribe.
    /// </summary>
    public static class VettingNoticeContent
    {
        public const string SupportEmail = "support@oncarepro.com";

        public static VettingNoticeMessage Build(string type, string firstName, IReadOnlyList<string> missing, DateTime deadlineUtc,
            DateTime? clearanceDeadlineUtc, int clearanceMaxAgeMonths, string hubUrl)
        {
            var name = string.IsNullOrWhiteSpace(firstName) ? "there" : firstName.Trim();
            var when = LagosTime.Format(deadlineUtc);
            var shortDate = LagosTime.FromUtc(deadlineUtc).ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

            var subject = type switch
            {
                VettingNoticeTypes.Reminder7d => "Reminder: 7 days left to complete your CarePro vetting",
                VettingNoticeTypes.Reminder1d => "Reminder: 1 day left to complete your CarePro vetting",
                _ => $"Action needed: complete your CarePro vetting by {shortDate}",
            };
            var intro = type switch
            {
                VettingNoticeTypes.Reminder7d => "This is a reminder: there are 7 days left to finish the vetting steps below.",
                VettingNoticeTypes.Reminder1d => "This is a reminder: there is 1 day left to finish the vetting steps below.",
                _ => "To keep your caregiver account fully available, please finish the vetting steps below.",
            };

            var steps = new List<(string Title, string Why)>();
            foreach (var m in missing)
            {
                switch (m)
                {
                    case VettingSteps.Classification:
                        steps.Add(("Set your caregiver type", "Choose whether you are an auxiliary nurse, a CHEW or a registered nurse.")); break;
                    case VettingSteps.Guarantors:
                        steps.Add(("Add two guarantors", "They only need to be added now; their confirmation can come later.")); break;
                    case VettingSteps.AddressHistory:
                        steps.Add(("Complete your address history", "List the places you have lived over the last 5 years, with no gaps.")); break;
                    case VettingSteps.PoliceClearance:
                    {
                        var by = clearanceDeadlineUtc.HasValue && clearanceDeadlineUtc.Value != deadlineUtc
                            ? $" It is needed by {LagosTime.Format(clearanceDeadlineUtc.Value)}." : string.Empty;
                        steps.Add(("Submit your police clearance",
                            $"Upload a recent clearance from the Nigeria Police Force, issued within the last {clearanceMaxAgeMonths} months. It only needs to be submitted now; our team reviews it afterwards.{by}"));
                        break;
                    }
                }
            }

            var consequence = $"After {when}, until these steps are done, you will only be able to open the vetting steps, your wallet and withdrawals, settings, messages and notifications.";

            // ── plain text: real line breaks ──
            var t = new StringBuilder();
            t.Append($"Dear {name},\n\n{intro}\n\nSteps still missing on your account:\n\n");
            for (var i = 0; i < steps.Count; i++) t.Append($"{i + 1}. {steps[i].Title}. {steps[i].Why}\n");
            t.Append($"\nDeadline: {when}.\n{consequence}\n\nComplete them here: {hubUrl}\n\nQuestions? Contact {SupportEmail}.\n\nThanks,\nThe CarePro Team\n");

            // ── HTML ──
            string E(string s) => WebUtility.HtmlEncode(s);
            var h = new StringBuilder();
            h.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.5;color:#222\">");
            h.Append($"<p>Dear {E(name)},</p><p>{E(intro)}</p><p><strong>Steps still missing on your account:</strong></p><ol>");
            foreach (var s in steps) h.Append($"<li><strong>{E(s.Title)}.</strong> {E(s.Why)}</li>");
            h.Append($"</ol><p><strong>Deadline:</strong> {E(when)}.<br />{E(consequence)}</p>");
            h.Append($"<p><a href=\"{E(hubUrl)}\" style=\"display:inline-block;background:#1e3a5f;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none\">Open my vetting steps</a></p>");
            h.Append($"<p>Questions? Contact <a href=\"mailto:{SupportEmail}\">{SupportEmail}</a>.</p><p>Thanks,<br />The CarePro Team</p></div>");

            var inApp = $"Finish {steps.Count} vetting step{(steps.Count == 1 ? "" : "s")} by {shortDate}: {string.Join("; ", steps.Select(s => s.Title))}. After that date your account is limited to the vetting steps, wallet, settings, messages and notifications until they are done.";
            return new VettingNoticeMessage(subject, h.ToString(), t.ToString(), $"Vetting steps needed by {shortDate}", inApp);
        }
    }
}
