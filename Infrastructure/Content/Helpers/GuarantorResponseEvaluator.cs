using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Infrastructure.Content.Helpers
{
    /// <summary>
    /// The soft-gate auto-checks on a guarantor's questionnaire. Pure: it compares what the guarantor
    /// said with what the caregiver declared and returns the names of the checks that did not pass.
    /// An empty list means the guarantor can be confirmed without staff review. The caregiver's area
    /// answer is deliberately not checked — it is shown to staff only.
    /// </summary>
    public static class GuarantorResponseEvaluator
    {
        /// <summary>A guarantor must have known the caregiver at least this long.</summary>
        public const int MinimumYearsKnown = 2;

        /// <summary>How far the guarantor's years may drift from the caregiver's claim before it is flagged.</summary>
        public const int YearsTolerance = 2;

        /// <summary>Shortest acceptable "how do you know them" answer, in words.</summary>
        public const int MinimumHowTheyKnowWords = 25;

        public static int WordCount(string? text) =>
            string.IsNullOrWhiteSpace(text)
                ? 0
                : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        public static List<string> Evaluate(Guarantor declared, Caregiver caregiver, GuarantorAnswers answers)
        {
            var flags = new List<string>();

            if (!NameMatches(caregiver.FirstName, caregiver.LastName, answers.CaregiverName))
                flags.Add(GuarantorCheckNames.NameMismatch);

            // Guarantors added before the questionnaire have nothing declared to compare against,
            // so they can never auto-confirm — staff read the answers instead.
            if (string.IsNullOrWhiteSpace(declared.RelationshipCategory) || declared.YearsKnown == null)
            {
                flags.Add(GuarantorCheckNames.NoDeclaredData);
            }
            else
            {
                if (!string.Equals(declared.RelationshipCategory, answers.RelationshipCategory, StringComparison.Ordinal))
                    flags.Add(GuarantorCheckNames.RelationshipMismatch);

                if (Math.Abs(declared.YearsKnown.Value - answers.YearsKnown) > YearsTolerance)
                    flags.Add(GuarantorCheckNames.YearsMismatch);
            }

            if (answers.YearsKnown < MinimumYearsKnown)
                flags.Add(GuarantorCheckNames.YearsBelowMinimum);

            if (string.Equals(answers.LastContact, GuarantorLastContact.OverYear, StringComparison.Ordinal))
                flags.Add(GuarantorCheckNames.StaleContact);

            if (!string.Equals(answers.TrustAnswer, GuarantorTrustAnswers.Yes, StringComparison.Ordinal))
                flags.Add(GuarantorCheckNames.TrustReservation);

            if (!string.IsNullOrWhiteSpace(answers.PhoneNo) && !PhonesMatch(declared.PhoneNo, answers.PhoneNo))
                flags.Add(GuarantorCheckNames.PhoneMismatch);

            return flags;
        }

        /// <summary>
        /// Order-insensitive: the caregiver's first and last name must each appear among the typed words
        /// (a middle name is optional). Case, accents and punctuation are ignored, and a name of five or
        /// more letters tolerates one typo.
        /// </summary>
        public static bool NameMatches(string? firstName, string? lastName, string? typed)
        {
            var typedTokens = Tokenize(typed);
            var required = Tokenize(firstName).Concat(Tokenize(lastName)).ToList();
            if (required.Count == 0 || typedTokens.Count == 0) return false;
            return required.All(r => typedTokens.Any(t => TokenMatches(r, t)));
        }

        /// <summary>Compares the last 10 digits so "+234 801…" and "0801…" are the same number.</summary>
        public static bool PhonesMatch(string? declared, string? typed)
        {
            var a = LastDigits(declared);
            var b = LastDigits(typed);
            return a.Length > 0 && a == b;
        }

        private static string LastDigits(string? phone)
        {
            var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
            return digits.Length > 10 ? digits[^10..] : digits;
        }

        private static bool TokenMatches(string required, string typed)
        {
            if (required == typed) return true;
            return required.Length >= 5 && typed.Length >= 5 && Levenshtein(required, typed) <= 1;
        }

        private static List<string> Tokenize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new List<string>();
            var decomposed = value.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetter(ch) ? char.ToLowerInvariant(ch) : ' ');
            }
            return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static int Levenshtein(string a, string b)
        {
            var prev = new int[b.Length + 1];
            var curr = new int[b.Length + 1];
            for (var j = 0; j <= b.Length; j++) prev[j] = j;
            for (var i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }
            return prev[b.Length];
        }
    }

    /// <summary>The fields of a submitted questionnaire the evaluator needs.</summary>
    public class GuarantorAnswers
    {
        public string CaregiverName { get; set; } = string.Empty;
        public string RelationshipCategory { get; set; } = string.Empty;
        public int YearsKnown { get; set; }
        public string LastContact { get; set; } = string.Empty;
        public string TrustAnswer { get; set; } = string.Empty;
        public string? PhoneNo { get; set; }
    }
}
