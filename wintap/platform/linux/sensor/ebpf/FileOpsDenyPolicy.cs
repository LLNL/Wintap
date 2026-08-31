using System;
using System.Collections.Generic;
using System.Text;

namespace gov.llnl.wintap.platform.linux.collect
{
    internal static class FileOpsDenyPolicy
    {
        internal const int MaxRules = 15;
        internal const int MaxCommBytes = 15;

        internal static IReadOnlyList<string> Parse(string? configuredRules)
        {
            var rules = new List<string>();
            if (string.IsNullOrWhiteSpace(configuredRules))
            {
                return rules;
            }

            foreach (string rawRule in configuredRules.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string rule = rawRule.Trim();
                if (string.IsNullOrEmpty(rule))
                {
                    continue;
                }
                if (Encoding.UTF8.GetByteCount(rule) > MaxCommBytes)
                {
                    throw new ArgumentException($"FileOps deny comm '{rule}' exceeds the {MaxCommBytes}-byte Linux comm limit.");
                }
                if (!rules.Exists(existing => string.Equals(existing, rule, StringComparison.Ordinal)))
                {
                    rules.Add(rule);
                }
            }

            if (rules.Count > MaxRules)
            {
                throw new ArgumentException($"FileOps deny policy supports at most {MaxRules} exact comm rules.");
            }

            return rules;
        }
    }
}
