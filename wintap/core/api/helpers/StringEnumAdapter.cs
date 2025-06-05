using gov.llnl.wintap.collect.models;
using com.espertech.esper.common.client;
using System;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using global::gov.llnl.wintap.collect.models;

namespace gov.llnl.wintap.core.api.helpers
{
    /// <summary>
    /// Provides conversion between enum types and string representations
    /// to maintain compatibility between C# code and Esper queries
    /// </summary>
    public static class StringEnumAdapter
    {
        // Cache enum values to avoid repeated reflection
        private static readonly Dictionary<string, int> _messageTypeCache;
        private static readonly Dictionary<string, int> _activityTypeCache;
        private static readonly Dictionary<int, string> _messageTypeReverseCache;
        private static readonly Dictionary<int, string> _activityTypeReverseCache;

        // Initialize caches
        static StringEnumAdapter()
        {
            _messageTypeCache = Enum.GetValues(typeof(WintapMessage.MessageTypeEnum))
                .Cast<WintapMessage.MessageTypeEnum>()
                .ToDictionary(e => e.ToString(), e => (int)e);

            _activityTypeCache = Enum.GetValues(typeof(WintapMessage.ActivityTypeEnum))
                .Cast<WintapMessage.ActivityTypeEnum>()
                .ToDictionary(e => e.ToString(), e => (int)e);

            _messageTypeReverseCache = _messageTypeCache.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
            _activityTypeReverseCache = _activityTypeCache.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
        }

        /// <summary>
        /// Intercepts Esper queries and converts string-based message type and activity type
        /// comparisons to enum-based comparisons
        /// </summary>
        /// <param name="eplStatement">The original EPL statement with string literals</param>
        /// <returns>A modified EPL statement that works with enum types</returns>
        public static string AdaptQueryStringToEnums(string eplStatement)
        {
            if (string.IsNullOrEmpty(eplStatement))
                return eplStatement;

            // Process equality comparisons (=)
            eplStatement = ProcessMessageTypeComparisons(eplStatement);
            eplStatement = ProcessActivityTypeComparisons(eplStatement);

            // Process IN list expressions
            eplStatement = ProcessMessageTypeInLists(eplStatement);
            eplStatement = ProcessActivityTypeInLists(eplStatement);

            // Process NOT equality comparisons (!=)
            eplStatement = ProcessMessageTypeNotComparisons(eplStatement);
            eplStatement = ProcessActivityTypeNotComparisons(eplStatement);

            return eplStatement;
        }

        /// <summary>
        /// Processes MessageType string comparisons and converts them to enum comparisons
        /// </summary>
        private static string ProcessMessageTypeComparisons(string eplStatement)
        {
            // Pattern to match MessageType = "STRING" or MessageType = 'STRING'
            var messageTypePattern = new Regex(@"(MessageType\s*=\s*['""])([^'""]+)(['""])");

            return messageTypePattern.Replace(eplStatement, match =>
            {
                var prefix = match.Groups[1].Value;
                var typeString = match.Groups[2].Value;
                var suffix = match.Groups[3].Value;

                // Check if the string value exists in our cache
                if (_messageTypeCache.TryGetValue(typeString, out int enumValue))
                {
                    // Convert to enum-based comparison
                    return $"MessageType = {enumValue} /* {typeString} */";
                }

                // If not recognized, leave as is
                return match.Value;
            });
        }

        /// <summary>
        /// Processes ActivityType string comparisons and converts them to enum comparisons
        /// </summary>
        private static string ProcessActivityTypeComparisons(string eplStatement)
        {
            // Pattern to match ActivityType = "STRING" or ActivityType = 'STRING'
            var activityTypePattern = new Regex(@"(ActivityType\s*=\s*['""])([^'""]+)(['""])");

            return activityTypePattern.Replace(eplStatement, match =>
            {
                var prefix = match.Groups[1].Value;
                var typeString = match.Groups[2].Value;
                var suffix = match.Groups[3].Value;

                // Check if the string value exists in our cache
                if (_activityTypeCache.TryGetValue(typeString, out int enumValue))
                {
                    // Convert to enum-based comparison
                    return $"ActivityType = {enumValue} /* {typeString} */";
                }

                // If not recognized, leave as is
                return match.Value;
            });
        }

        /// <summary>
        /// Processes MessageType NOT comparisons and converts them to enum comparisons
        /// </summary>
        private static string ProcessMessageTypeNotComparisons(string eplStatement)
        {
            // Pattern to match MessageType != "STRING" or MessageType <> 'STRING'
            var messageTypePattern = new Regex(@"(MessageType\s*(?:!=|<>)\s*['""])([^'""]+)(['""])");

            return messageTypePattern.Replace(eplStatement, match =>
            {
                var prefix = match.Groups[1].Value;
                var typeString = match.Groups[2].Value;
                var suffix = match.Groups[3].Value;

                // Check if the string value exists in our cache
                if (_messageTypeCache.TryGetValue(typeString, out int enumValue))
                {
                    // Get the operator (either != or <>)
                    string op = prefix.Contains("!=") ? "!=" : "<>";

                    // Convert to enum-based comparison
                    return $"MessageType {op} {enumValue} /* {typeString} */";
                }

                // If not recognized, leave as is
                return match.Value;
            });
        }

        /// <summary>
        /// Processes ActivityType NOT comparisons and converts them to enum comparisons
        /// </summary>
        private static string ProcessActivityTypeNotComparisons(string eplStatement)
        {
            // Pattern to match ActivityType != "STRING" or ActivityType <> 'STRING'
            var activityTypePattern = new Regex(@"(ActivityType\s*(?:!=|<>)\s*['""])([^'""]+)(['""])");

            return activityTypePattern.Replace(eplStatement, match =>
            {
                var prefix = match.Groups[1].Value;
                var typeString = match.Groups[2].Value;
                var suffix = match.Groups[3].Value;

                // Check if the string value exists in our cache
                if (_activityTypeCache.TryGetValue(typeString, out int enumValue))
                {
                    // Get the operator (either != or <>)
                    string op = prefix.Contains("!=") ? "!=" : "<>";

                    // Convert to enum-based comparison
                    return $"ActivityType {op} {enumValue} /* {typeString} */";
                }

                // If not recognized, leave as is
                return match.Value;
            });
        }

        /// <summary>
        /// Processes MessageType IN list expressions
        /// </summary>
        private static string ProcessMessageTypeInLists(string eplStatement)
        {
            // Pattern to match MessageType IN ("STRING1", "STRING2", ...)
            var inListPattern = new Regex(@"(MessageType\s+IN\s*\(\s*)([^)]+)(\s*\))");

            return inListPattern.Replace(eplStatement, match =>
            {
                var prefix = match.Groups[1].Value;
                var itemList = match.Groups[2].Value;
                var suffix = match.Groups[3].Value;

                // Split the item list and process each string
                var items = itemList.Split(',');
                var processedItems = new List<string>();

                foreach (var item in items)
                {
                    // Extract the string value from quotes
                    var stringMatch = Regex.Match(item.Trim(), @"['""]([^'""]+)['""]");
                    if (stringMatch.Success)
                    {
                        var typeString = stringMatch.Groups[1].Value;
                        if (_messageTypeCache.TryGetValue(typeString, out int enumValue))
                        {
                            // Add the enum value with comment
                            processedItems.Add($"{enumValue} /* {typeString} */");
                        }
                        else
                        {
                            // Keep original if not recognized
                            processedItems.Add(item.Trim());
                        }
                    }
                    else
                    {
                        // Keep original if not a string
                        processedItems.Add(item.Trim());
                    }
                }

                // Join the processed items
                return prefix + string.Join(", ", processedItems) + suffix;
            });
        }

        /// <summary>
        /// Processes ActivityType IN list expressions
        /// </summary>
        private static string ProcessActivityTypeInLists(string eplStatement)
        {
            // Pattern to match ActivityType IN ("STRING1", "STRING2", ...)
            var inListPattern = new Regex(@"(ActivityType\s+IN\s*\(\s*)([^)]+)(\s*\))");

            return inListPattern.Replace(eplStatement, match =>
            {
                var prefix = match.Groups[1].Value;
                var itemList = match.Groups[2].Value;
                var suffix = match.Groups[3].Value;

                // Split the item list and process each string
                var items = itemList.Split(',');
                var processedItems = new List<string>();

                foreach (var item in items)
                {
                    // Extract the string value from quotes
                    var stringMatch = Regex.Match(item.Trim(), @"['""]([^'""]+)['""]");
                    if (stringMatch.Success)
                    {
                        var typeString = stringMatch.Groups[1].Value;
                        if (_activityTypeCache.TryGetValue(typeString, out int enumValue))
                        {
                            // Add the enum value with comment
                            processedItems.Add($"{enumValue} /* {typeString} */");
                        }
                        else
                        {
                            // Keep original if not recognized
                            processedItems.Add(item.Trim());
                        }
                    }
                    else
                    {
                        // Keep original if not a string
                        processedItems.Add(item.Trim());
                    }
                }

                // Join the processed items
                return prefix + string.Join(", ", processedItems) + suffix;
            });
        }

        /// <summary>
        /// Converts an enum value back to its string representation for display
        /// </summary>
        /// <param name="enumType">The enum type (MessageType or ActivityType)</param>
        /// <param name="enumValue">The numeric enum value</param>
        /// <returns>String representation of the enum value</returns>
        public static string GetEnumStringValue(string enumType, int enumValue)
        {
            if (string.Equals(enumType, "MessageType", StringComparison.OrdinalIgnoreCase))
            {
                if (_messageTypeReverseCache.TryGetValue(enumValue, out string stringValue))
                {
                    return stringValue;
                }
            }
            else if (string.Equals(enumType, "ActivityType", StringComparison.OrdinalIgnoreCase))
            {
                if (_activityTypeReverseCache.TryGetValue(enumValue, out string stringValue))
                {
                    return stringValue;
                }
            }

            return enumValue.ToString();
        }

        /// <summary>
        /// Formats an Esper event for display by converting enum values to strings
        /// </summary>
        /// <param name="eventString">The event string to format</param>
        /// <returns>A formatted event string with readable enum values</returns>
        public static string FormatEventForDisplay(string eventString)
        {
            if (string.IsNullOrEmpty(eventString))
                return eventString;

            // Replace MessageType enum values with strings
            var messageTypePattern = new Regex(@"(MessageType\s*=\s*)(\d+)");
            eventString = messageTypePattern.Replace(eventString, match =>
            {
                var prefix = match.Groups[1].Value;
                if (int.TryParse(match.Groups[2].Value, out int enumValue))
                {
                    if (_messageTypeReverseCache.TryGetValue(enumValue, out string stringValue))
                    {
                        return $"{prefix}\"{stringValue}\"";
                    }
                }
                return match.Value;
            });

            // Replace ActivityType enum values with strings
            var activityTypePattern = new Regex(@"(ActivityType\s*=\s*)(\d+)");
            eventString = activityTypePattern.Replace(eventString, match =>
            {
                var prefix = match.Groups[1].Value;
                if (int.TryParse(match.Groups[2].Value, out int enumValue))
                {
                    if (_activityTypeReverseCache.TryGetValue(enumValue, out string stringValue))
                    {
                        return $"{prefix}\"{stringValue}\"";
                    }
                }
                return match.Value;
            });

            return eventString;
        }

        /// <summary>
        /// Adapts event results to include string representations of enum values
        /// </summary>
        /// <param name="eventBean">The event bean from Esper</param>
        /// <returns>Modified event with string-based type information</returns>
        public static EventBean AdaptEventForDisplay(EventBean eventBean)
        {
            // In a future implementation, this could modify the event bean
            // to include string representations of enum values before sending
            // to the frontend. For now, FormatEventForDisplay handles this
            // using string manipulation.
            return eventBean;
        }
    }
}