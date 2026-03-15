using System.Text.RegularExpressions;
using Application.DTOs;
using Application.Interfaces.Common;

namespace Infrastructure.Services.Common
{
    /// <summary>
    /// Sophisticated contact-information detector with multi-layer evasion resistance.
    /// Catches standard, obfuscated, leet-speak, Unicode-trick, spelled-out, and
    /// intent-based contact-sharing attempts in chat messages.
    /// </summary>
    public class ContactPatternDetector : IContactPatternDetector
    {
        // ═══════════════════════════════════════════════════════════════════
        //  LAYER 1: PHONE NUMBERS
        //  Standard, spaced, obfuscated, split, spelled-out, mixed
        // ═══════════════════════════════════════════════════════════════════

        // Nigerian mobile: 0[789][01]XXXXXXXX or +234[789][01]XXXXXXXX with separators
        private static readonly Regex NigerianMobilePattern = new(
            @"(?:\+?234[\s.\-]?|0)[789][01][\s.\-]?\d[\s.\-]?\d[\s.\-]?\d[\s.\-]?\d[\s.\-]?\d[\s.\-]?\d[\s.\-]?\d[\s.\-]?\d",
            RegexOptions.Compiled);

        // +234 followed by any digits (landlines, VOIP, etc.)
        private static readonly Regex NigerianCountryCodePattern = new(
            @"\+234[\s.\-]?\d(?:[\s.\-]?\d){7,9}",
            RegexOptions.Compiled);

        // Spaced Nigerian mobile with prefix after normalization
        private static readonly Regex SpacedNigerianPattern = new(
            @"(?<!\d)(?:\+?2[\s.\-]?3[\s.\-]?4[\s.\-]?)?0[\s.\-]?[789][\s.\-]?[01](?:[\s.\-]?\d){8}(?!\d)",
            RegexOptions.Compiled);

        // Digits split by exotic separators: commas, slashes, pipes, underscores
        // e.g. "080,1234,5678" or "080/1234/5678"
        private static readonly Regex SplitDigitsPattern = new(
            @"(?<!\w)\+?\d(?:[\s.\-,/_|\\]?\d){6,13}(?!\w)",
            RegexOptions.Compiled);

        // Generic: any 7+ consecutive digits with standard separators
        private static readonly Regex GenericDigitSequencePattern = new(
            @"(?<!\w)\+?\d(?:[\s.\-]?\d){6,13}(?!\w)",
            RegexOptions.Compiled);

        // Spelled-out digits: "zero eight zero one two three four five six seven eight"
        private static readonly Regex SpelledOutDigitsPattern = new(
            @"\b(?:zero|oh|one|two|three|four|five|six|seven|eight|nine)(?:\s+(?:zero|oh|one|two|three|four|five|six|seven|eight|nine)){5,12}\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Mixed spelled + numeric: "zero 8 zero 1 2 3 4 5 6 7 8"
        private static readonly Regex MixedSpelledDigitsPattern = new(
            @"\b(?:(?:zero|oh|one|two|three|four|five|six|seven|eight|nine|\d)[\s,.\-]+){5,12}(?:zero|oh|one|two|three|four|five|six|seven|eight|nine|\d)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ═══════════════════════════════════════════════════════════════════
        //  LAYER 2: EMAIL ADDRESSES
        //  Standard + obfuscated (at/dot text substitutions)
        // ═══════════════════════════════════════════════════════════════════

        // Standard email
        private static readonly Regex EmailPattern = new(
            @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
            RegexOptions.Compiled);

        // Obfuscated: "user at gmail dot com", "user [at] gmail [dot] com"
        private static readonly Regex ObfuscatedEmailPattern = new(
            @"[a-zA-Z0-9._%+\-]+\s*[\[\(]?\s*(?:at|@)\s*[\]\)]?\s*[a-zA-Z0-9.\-]+\s*[\[\(]?\s*(?:dot|\.)\s*[\]\)]?\s*[a-zA-Z]{2,}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ═══════════════════════════════════════════════════════════════════
        //  LAYER 3: URLS AND LINKS
        //  Standard, obfuscated, typo'd, domain-only, comma-domains
        // ═══════════════════════════════════════════════════════════════════

        // Standard WhatsApp links
        private static readonly Regex WhatsAppLinkPattern = new(
            @"(?:https?://)?(?:wa\.me|chat\.whatsapp\.com|api\.whatsapp\.com)[/\w.\-?=&]*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Standard social media full URLs
        private static readonly Regex SocialMediaLinkPattern = new(
            @"(?:https?://)?(?:www\.)?(?:instagram\.com|t\.me|telegram\.me|facebook\.com|fb\.com|twitter\.com|x\.com|tiktok\.com|snapchat\.com|linkedin\.com/in|youtube\.com|youtu\.be|pinterest\.com|reddit\.com|threads\.net)[/\w.\-?=&@]*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Obfuscated URLs: typo'd protocols, extra letters
        // "htttps://f,me", "htp://facbook,com", "httttps://wa.me"
        private static readonly Regex ObfuscatedUrlPattern = new(
            @"h{1,3}t{1,3}p{0,2}s?\s*:\s*/{1,3}\s*[\w,.\s/\-?=&@]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Domain with commas instead of dots: "facebook,com", "wa,me", "ig,com"
        private static readonly Regex CommaDomainPattern = new(
            @"\b(?:wa|ig|fb|tt|tg|whatsapp|instagram|facebook|facbook|facebk|twitter|telegram|tiktok|snapchat|linkedin|youtube|signal|viber|imo|wechat|skype)\s*[,]\s*(?:com|me|org|net|co|io|ng)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Domain with spaces instead of dots: "facebook . com", "wa . me"
        private static readonly Regex SpacedDomainPattern = new(
            @"\b(?:wa|ig|fb|tt|tg|whatsapp|instagram|facebook|facbook|facebk|twitter|telegram|tiktok|snapchat|linkedin|youtube|signal|viber|imo|wechat|skype)\s+(?:dot|\.|,)\s*(?:com|me|org|net|co|io|ng)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Generic URL: any word.tld or word.tld/path (catches unknown platforms too)
        private static readonly Regex GenericUrlPattern = new(
            @"(?:https?://)?(?:www\.)?[a-zA-Z0-9\-]+\.(?:com|me|org|net|co|io|app|link|bio|page|site|xyz|ng)(?:/[\w.\-?=&@%]*)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ═══════════════════════════════════════════════════════════════════
        //  LAYER 4: PLATFORM NAMES (standalone — no URL needed)
        //  Catches misspellings, spacing tricks, abbreviations
        // ═══════════════════════════════════════════════════════════════════

        // Full platform names with common misspellings and spacing evasions
        private static readonly Regex PlatformNamePattern = new(
            @"\b(?:" +
            // WhatsApp variants
            @"whatsapp|whats\s*app|watsapp|wats\s*app|whatapp|what\s*app|wazapp|wassap|wasap|w[ah]tsup|" +
            // Facebook variants
            @"facebook|face\s*book|facbook|facebk|faceb00k|" +
            // Instagram variants
            @"instagram|insta\s*gram|instgram|" +
            // Snapchat variants
            @"snapchat|snap\s*chat|" +
            // TikTok variants
            @"tiktok|tik\s*tok|" +
            // Telegram variants
            @"telegram|tele\s*gram|telgram|" +
            // Other platforms
            @"twitter|linkedin|linked\s*in|signal|viber|imo|wechat|we\s*chat|skype|" +
            @"line\s+app|threads|pinterest|youtube|you\s*tube" +
            @")\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Short abbreviations (2-letter codes commonly used)
        private static readonly Regex PlatformAbbrevPattern = new(
            @"\b(?:ig|fb|tw|tt|tg|wa|li|yt|snap)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ═══════════════════════════════════════════════════════════════════
        //  LAYER 5: CONTACT INTENT PHRASES
        //  Signals that the user wants to move communication off-platform
        // ═══════════════════════════════════════════════════════════════════

        private static readonly Regex ContactIntentPattern = new(
            @"\b(?:" +
            // Direct requests: "call me", "text me", "buzz me", etc.
            @"(?:call|text|buzz|ring|beep|flash|phone)\s+me|" +
            // "my X" possessive references
            @"my\s+(?:number|line|digits|contact|phone|cell|handle|page|profile|username|id|account|socials|pin|bbm)|" +
            // "on/via my" references
            @"(?:on|via|through)\s+my\s+(?:number|line|phone|contact|page|handle|profile|socials)|" +
            // Engagement requests
            @"reach\s+me|contact\s+me|dm\s+me|inbox\s+me|ping\s+me|message\s+me|" +
            @"hit\s+me\s+up|hmu|holla\s+(?:me|at\s+me)|holler\s+(?:me|at\s+me)|" +
            // Platform-directed actions: "send me on", "add me on", etc.
            @"(?:send|message|msg|text|chat|add|find|follow|check|look|search)\s+(?:me\s+)?(?:on|at|via|through|up\s+on)|" +
            // "link up" / "connect" via external
            @"(?:link|hook|connect|meet)\s+(?:up\s+)?(?:on|via|through|outside)|" +
            // "talk/chat outside the app"
            @"(?:talk|chat|reach|contact|connect|communicate)\s+(?:me\s+)?(?:outside|off|off\s+(?:this|the)\s+(?:app|platform|site))|" +
            // Direct "via platform" references
            @"via\s+(?:whatsapp|watsapp|whatapp|facebook|fb|instagram|insta|telegram|twitter|snapchat|tiktok|signal|viber|imo|wechat|skype|line|snap|ig|tg|wa|tt|yt)|" +
            // "I'm on platform" / "find me on"
            @"(?:i'?m|i\s+am|find\s+me|catch\s+me|get\s+me|search\s+(?:for\s+)?me)\s+(?:on|at)" +
            @")\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ═══════════════════════════════════════════════════════════════════
        //  LAYER 6: SOCIAL MEDIA HANDLES (@username)
        // ═══════════════════════════════════════════════════════════════════

        // @ handles — but excludes email addresses (no domain after)
        private static readonly Regex AtHandlePattern = new(
            @"@[a-zA-Z0-9_.]{2,30}(?![a-zA-Z0-9.]*@|\.[a-zA-Z]{2,})",
            RegexOptions.Compiled);

        // ═══════════════════════════════════════════════════════════════════
        //  DETECTION RULES — ordered specific → general to avoid false positives
        // ═══════════════════════════════════════════════════════════════════

        private static readonly (Regex Pattern, string Category)[] DetectionRules = new[]
        {
            // Phone numbers (specific Nigerian → generic sequences)
            (NigerianMobilePattern, "PhoneNumber"),
            (NigerianCountryCodePattern, "PhoneNumber"),
            (SpacedNigerianPattern, "PhoneNumber"),
            (SplitDigitsPattern, "PhoneNumber"),
            (GenericDigitSequencePattern, "PhoneNumber"),
            (SpelledOutDigitsPattern, "PhoneNumber"),
            (MixedSpelledDigitsPattern, "PhoneNumber"),

            // Email (standard → obfuscated)
            (EmailPattern, "Email"),
            (ObfuscatedEmailPattern, "Email"),

            // URLs and links (exact → obfuscated → generic)
            (WhatsAppLinkPattern, "ExternalLink"),
            (SocialMediaLinkPattern, "ExternalLink"),
            (ObfuscatedUrlPattern, "ExternalLink"),
            (CommaDomainPattern, "ExternalLink"),
            (SpacedDomainPattern, "ExternalLink"),
            (GenericUrlPattern, "ExternalLink"),

            // Platform names and abbreviations
            (PlatformNamePattern, "ContactKeyword"),
            (PlatformAbbrevPattern, "ContactKeyword"),

            // Contact intent phrases
            (ContactIntentPattern, "ContactKeyword"),

            // Social media handles
            (AtHandlePattern, "SocialHandle"),
        };

        // ═══════════════════════════════════════════════════════════════════
        //  MAIN DETECTION METHOD
        // ═══════════════════════════════════════════════════════════════════

        public ContactDetectionResult Detect(string message)
        {
            var result = new ContactDetectionResult
            {
                HasViolation = false,
                RedactedMessage = message
            };

            if (string.IsNullOrWhiteSpace(message))
                return result;

            // Step 1: Normalize Unicode tricks (fullwidth digits, zero-width chars)
            var cleaned = NormalizeUnicodeTricks(message);

            // Step 2: Normalize leet-speak for digit detection
            var normalized = NormalizeLeetSpeak(cleaned);

            foreach (var (pattern, category) in DetectionRules)
            {
                // Detect on fully normalized text
                var normalizedMatches = pattern.Matches(normalized);

                // Log detected patterns for violation records
                foreach (Match match in normalizedMatches)
                {
                    result.HasViolation = true;
                    result.Patterns.Add(new DetectedPattern
                    {
                        Category = category,
                        MatchedText = match.Value.Trim()
                    });
                }

                // Strip matched content using normalized positions on RedactedMessage.
                // Both NormalizeUnicodeTricks and NormalizeLeetSpeak are char-for-char
                // replacements, so character positions are 1:1 with the original.
                var normalizedRedacted = NormalizeLeetSpeak(NormalizeUnicodeTricks(result.RedactedMessage));
                var stripMatches = pattern.Matches(normalizedRedacted)
                    .Cast<Match>()
                    .OrderByDescending(m => m.Index)
                    .ToList();

                foreach (var match in stripMatches)
                {
                    var before = result.RedactedMessage[..match.Index];
                    var after = match.Index + match.Length <= result.RedactedMessage.Length
                        ? result.RedactedMessage[(match.Index + match.Length)..]
                        : "";
                    result.RedactedMessage = before + " " + after;
                }
            }

            // Clean up extra whitespace left by stripping
            result.RedactedMessage = Regex.Replace(result.RedactedMessage, @"\s{2,}", " ").Trim();

            return result;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  NORMALIZATION HELPERS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Replaces Unicode evasion tricks with ASCII equivalents.
        /// Handles fullwidth digits/letters, zero-width chars, etc.
        /// Preserves string length (char-for-char replacement).
        /// </summary>
        private static string NormalizeUnicodeTricks(string input)
        {
            var chars = input.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                // Fullwidth digits ０-９ → 0-9
                if (c >= '\uFF10' && c <= '\uFF19')
                    chars[i] = (char)('0' + (c - '\uFF10'));
                // Fullwidth uppercase Ａ-Ｚ → A-Z
                else if (c >= '\uFF21' && c <= '\uFF3A')
                    chars[i] = (char)('A' + (c - '\uFF21'));
                // Fullwidth lowercase ａ-ｚ → a-z
                else if (c >= '\uFF41' && c <= '\uFF5A')
                    chars[i] = (char)('a' + (c - '\uFF41'));
                // Fullwidth @ → @
                else if (c == '\uFF20')
                    chars[i] = '@';
                // Fullwidth dot → .
                else if (c == '\uFF0E')
                    chars[i] = '.';
                // Fullwidth comma → ,
                else if (c == '\uFF0C')
                    chars[i] = ',';
                // Fullwidth + → +
                else if (c == '\uFF0B')
                    chars[i] = '+';
                // Zero-width characters → space
                else if (c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\uFEFF' || c == '\u00AD')
                    chars[i] = ' ';
            }
            return new string(chars);
        }

        /// <summary>
        /// Normalizes leet-speak substitutions within digit contexts.
        /// Only converts letters that appear between or adjacent to digits,
        /// avoiding false positives in normal text.
        /// </summary>
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
                    'G' or 'g' when IsInDigitContext(input, i) => '9',
                    'A' or 'a' when IsInDigitContext(input, i) => '4',
                    'T' or 't' when IsInDigitContext(input, i) => '7',
                    'E' or 'e' when IsInDigitContext(input, i) => '3',
                    _ => chars[i]
                };
            }
            return new string(chars);
        }

        /// <summary>
        /// Returns true if the character at position i is surrounded by digits or digit separators,
        /// indicating it may be a leet-speak substitution within a phone number.
        /// </summary>
        private static bool IsInDigitContext(string input, int i)
        {
            bool hasDigitBefore = false;
            bool hasDigitAfter = false;

            // Look backwards, skipping common separators
            for (int j = i - 1; j >= 0 && j >= i - 4; j--)
            {
                if (char.IsDigit(input[j])) { hasDigitBefore = true; break; }
                if (input[j] != ' ' && input[j] != '-' && input[j] != '.' && input[j] != ',' && input[j] != '_') break;
            }

            // Look forwards, skipping common separators
            for (int j = i + 1; j < input.Length && j <= i + 4; j++)
            {
                if (char.IsDigit(input[j])) { hasDigitAfter = true; break; }
                if (input[j] != ' ' && input[j] != '-' && input[j] != '.' && input[j] != ',' && input[j] != '_') break;
            }

            return hasDigitBefore && hasDigitAfter;
        }
    }
}
