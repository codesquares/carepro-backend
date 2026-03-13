using System.Text.RegularExpressions;
using Application.DTOs;
using Application.Interfaces.Common;

namespace Infrastructure.Services.Common
{
    public class ContactPatternDetector : IContactPatternDetector
    {
        private const string Redaction = "[contact info removed]";

        // Nigerian phone numbers: 0[789][01]XXXXXXXX or +234[789][01]XXXXXXXX
        // Allows optional separators (spaces, dashes, dots) between digit groups
        private static readonly Regex NigerianPhonePattern = new(
            @"(?:\+?234[\s.\-]?|0)[789][01][\s.\-]?\d[\s.\-]?\d[\s.\-]?\d[\s.\-]?\d[\s.\-]?\d[\s.\-]?\d[\s.\-]?\d[\s.\-]?\d",
            RegexOptions.Compiled);

        // Spaced/obfuscated digit sequences: 10-11 digits separated by spaces/dots/dashes
        // Only matches when digits start with Nigerian prefixes after normalization
        private static readonly Regex SpacedDigitsPattern = new(
            @"(?<!\d)(?:\+?2[\s.\-]?3[\s.\-]?4[\s.\-]?)?0[\s.\-]?[789][\s.\-]?[01](?:[\s.\-]?\d){8}(?!\d)",
            RegexOptions.Compiled);

        // Email addresses
        private static readonly Regex EmailPattern = new(
            @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
            RegexOptions.Compiled);

        // WhatsApp links
        private static readonly Regex WhatsAppLinkPattern = new(
            @"(?:https?://)?(?:wa\.me|chat\.whatsapp\.com|api\.whatsapp\.com)[/\w.\-?=&]*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Social media / messaging platform links
        private static readonly Regex SocialMediaLinkPattern = new(
            @"(?:https?://)?(?:www\.)?(?:instagram\.com|t\.me|telegram\.me|facebook\.com|fb\.com|twitter\.com|x\.com|tiktok\.com|snapchat\.com|linkedin\.com/in)[/\w.\-?=&@]*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Contact keywords — common Nigerian phrasing for moving off-platform
        private static readonly Regex ContactKeywordPattern = new(
            @"\b(?:whatsapp|watsapp|whatapp|whats\s*app|wats\s*app|telegram|call\s+me|text\s+me|my\s+number|reach\s+me|contact\s+me|dm\s+me|inbox\s+me|ping\s+me|hit\s+me\s+up|hmu|send\s+me\s+(?:a\s+)?(?:message|msg|text)\s+on|add\s+me\s+on|find\s+me\s+on)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Number words to detect spelled-out Nigerian phone prefixes
        private static readonly Regex NumberWordsPattern = new(
            @"\b(?:zero|oh)\s+(?:eight|nine|seven)\s+(?:zero|one|oh)(?:\s+(?:zero|one|two|three|four|five|six|seven|eight|nine|oh)){7,8}\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly (Regex Pattern, string Category)[] DetectionRules = new[]
        {
            (NigerianPhonePattern, "PhoneNumber"),
            (SpacedDigitsPattern, "PhoneNumber"),
            (EmailPattern, "Email"),
            (WhatsAppLinkPattern, "ExternalLink"),
            (SocialMediaLinkPattern, "ExternalLink"),
            (ContactKeywordPattern, "ContactKeyword"),
            (NumberWordsPattern, "PhoneNumber"),
        };

        public ContactDetectionResult Detect(string message)
        {
            var result = new ContactDetectionResult
            {
                HasViolation = false,
                RedactedMessage = message
            };

            if (string.IsNullOrWhiteSpace(message))
                return result;

            // Normalize common leet-speak substitutions for detection only
            var normalized = NormalizeLeetSpeak(message);

            foreach (var (pattern, category) in DetectionRules)
            {
                var matches = pattern.Matches(normalized);
                foreach (Match match in matches)
                {
                    result.HasViolation = true;
                    result.Patterns.Add(new DetectedPattern
                    {
                        Category = category,
                        MatchedText = match.Value.Trim()
                    });
                }

                // Redact from the actual message (not normalized) using same pattern
                result.RedactedMessage = pattern.Replace(result.RedactedMessage, Redaction);
            }

            return result;
        }

        private static string NormalizeLeetSpeak(string input)
        {
            var chars = input.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = chars[i] switch
                {
                    'O' or 'o' when IsInDigitContext(input, i) => '0',
                    'I' or 'l' when IsInDigitContext(input, i) => '1',
                    'B' when IsInDigitContext(input, i) => '8',
                    'S' or 's' when IsInDigitContext(input, i) => '5',
                    _ => chars[i]
                };
            }
            return new string(chars);
        }

        /// <summary>
        /// Returns true if the character at position i is surrounded by digits or digit separators,
        /// suggesting it may be a leet-speak substitution within a number.
        /// </summary>
        private static bool IsInDigitContext(string input, int i)
        {
            bool hasDigitBefore = false;
            bool hasDigitAfter = false;

            // Look backwards (skip spaces/dashes/dots)
            for (int j = i - 1; j >= 0 && j >= i - 3; j--)
            {
                if (char.IsDigit(input[j])) { hasDigitBefore = true; break; }
                if (input[j] != ' ' && input[j] != '-' && input[j] != '.') break;
            }

            // Look forwards (skip spaces/dashes/dots)
            for (int j = i + 1; j < input.Length && j <= i + 3; j++)
            {
                if (char.IsDigit(input[j])) { hasDigitAfter = true; break; }
                if (input[j] != ' ' && input[j] != '-' && input[j] != '.') break;
            }

            return hasDigitBefore && hasDigitAfter;
        }
    }
}
