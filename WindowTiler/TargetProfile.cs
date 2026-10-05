using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace MoliWindowTiler
{
    [DataContract]
    internal sealed class TargetProfile
    {
        internal const string MoliDefaultId = "moli-default";
        internal const string IdentityMoli = "moli";
        internal const string IdentityTitle = "title";
        internal const string IdentityThread = "thread";
        internal const string IdentityTitleSuffix = "titleSuffix";
        internal const string IdentityTitleRegex = "titleRegex";
        internal const string IdentityThreadRegex = "threadRegex";

        [DataMember(Name = "id", Order = 1)] public string Id;
        [DataMember(Name = "name", Order = 2)] public string Name;
        [DataMember(Name = "executableName", Order = 3)] public string ExecutableName;
        [DataMember(Name = "windowClass", Order = 4)] public string WindowClass;
        [DataMember(Name = "titleContains", Order = 5)] public string TitleContains;
        [DataMember(Name = "identitySource", Order = 6)] public string IdentitySource;
        [DataMember(Name = "identityPattern", Order = 7)] public string IdentityPattern;
        [DataMember(Name = "allowClose", Order = 8)] public bool AllowClose = true;
        [DataMember(Name = "enabled", Order = 9)] public bool Enabled = true;

        internal static TargetProfile CreateMoliDefault()
        {
            return new TargetProfile
            {
                Id = MoliDefaultId,
                Name = "魔力宝贝",
                ExecutableName = "Reincarnation.exe",
                WindowClass = "Reincar",
                TitleContains = "",
                IdentitySource = IdentityMoli,
                IdentityPattern = "",
                AllowClose = true,
                Enabled = true
            };
        }

        internal TargetProfile Clone()
        {
            return new TargetProfile
            {
                Id = Id,
                Name = Name,
                ExecutableName = ExecutableName,
                WindowClass = WindowClass,
                TitleContains = TitleContains,
                IdentitySource = IdentitySource,
                IdentityPattern = IdentityPattern,
                AllowClose = AllowClose,
                Enabled = Enabled
            };
        }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Name) ? (Id ?? "未命名目标") : Name;
        }

        internal bool Matches(string executableName, string windowClass, string title)
        {
            if (!Enabled) return false;
            if (!MatchesExecutableAndClass(executableName, windowClass)) return false;
            return MatchesTitle(title);
        }

        internal bool MatchesTitle(string title)
        {
            if (!Enabled) return false;
            if (!string.IsNullOrWhiteSpace(TitleContains) &&
                (title ?? "").IndexOf(TitleContains.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            return true;
        }

        internal bool MatchesExecutableAndClass(string executableName, string windowClass)
        {
            if (!Enabled) return false;
            if (!string.IsNullOrWhiteSpace(ExecutableName) &&
                !string.Equals(Path.GetFileName(ExecutableName.Trim()),
                    Path.GetFileName(executableName ?? ""), StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrWhiteSpace(WindowClass) &&
                !string.Equals(WindowClass.Trim(), (windowClass ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        internal List<string> ExtractIdentityCandidates(string title, string threadDescription, uint threadId)
        {
            string source = string.IsNullOrWhiteSpace(IdentitySource) ? IdentityMoli : IdentitySource;
            if (string.Equals(source, IdentityMoli, StringComparison.OrdinalIgnoreCase))
                return Native.CharacterCandidates(title, threadDescription, threadId);

            List<string> result = new List<string>();
            if (string.Equals(source, IdentityThread, StringComparison.OrdinalIgnoreCase))
                AddCandidate(result, threadDescription);
            else if (string.Equals(source, IdentityTitle, StringComparison.OrdinalIgnoreCase))
                AddCandidate(result, title);
            else if (string.Equals(source, IdentityTitleSuffix, StringComparison.OrdinalIgnoreCase))
                AddCandidate(result, SuffixFromTitle(title));
            else if (string.Equals(source, IdentityTitleRegex, StringComparison.OrdinalIgnoreCase))
                AddRegexCandidate(result, title, IdentityPattern);
            else if (string.Equals(source, IdentityThreadRegex, StringComparison.OrdinalIgnoreCase))
                AddRegexCandidate(result, threadDescription, IdentityPattern);
            else
                return Native.CharacterCandidates(title, threadDescription, threadId);

            if (result.Count == 0)
                AddCandidate(result, "未命名-线程" + threadId);
            return result;
        }

        private static void AddRegexCandidate(List<string> result, string source, string pattern)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(pattern)) return;
            try
            {
                Match match = Regex.Match(source, pattern, RegexOptions.IgnoreCase);
                if (!match.Success) return;
                Group identity = match.Groups["identity"];
                AddCandidate(result, identity != null && identity.Success ? identity.Value :
                    match.Groups.Count > 1 ? match.Groups[1].Value : match.Value);
            }
            catch (ArgumentException)
            {
                // An invalid rule is kept visible in the profile editor and falls back to the thread key.
            }
        }

        private static string SuffixFromTitle(string title)
        {
            string value = Clean(title);
            if (string.IsNullOrWhiteSpace(value)) return "";
            int separator = value.LastIndexOf("--", StringComparison.Ordinal);
            if (separator >= 0 && separator + 2 < value.Length) return value.Substring(separator + 2);
            int closingBracket = value.LastIndexOf(']');
            if (closingBracket >= 0 && closingBracket + 1 < value.Length) return value.Substring(closingBracket + 1);
            int closingParenthesis = value.LastIndexOf(')');
            int openingParenthesis = value.LastIndexOf('(');
            if (openingParenthesis >= 0 && closingParenthesis > openingParenthesis + 1)
                return value.Substring(openingParenthesis + 1, closingParenthesis - openingParenthesis - 1);
            int dash = value.LastIndexOf('-');
            return dash >= 0 && dash + 1 < value.Length ? value.Substring(dash + 1) : value;
        }

        private static void AddCandidate(List<string> result, string value)
        {
            value = Clean(value);
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, "Reincarnation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "cg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "主线程", StringComparison.OrdinalIgnoreCase)) return;
            if (!result.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
                result.Add(value);
        }

        private static string Clean(string value)
        {
            return (value ?? "").Trim().Trim('-', ' ', '\t', '[', ']');
        }
    }
}
