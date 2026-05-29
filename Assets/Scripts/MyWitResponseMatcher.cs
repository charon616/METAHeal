/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * This source code is licensed under the license found in the
 * LICENSE file in the root directory of this source tree.
 */

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Meta.WitAi.Attributes;
using Meta.WitAi.Data;
using Meta.WitAi.Json;
using Meta.WitAi.Utilities;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace Meta.WitAi.CallbackHandlers
{
    /// <summary>
    /// Project-specific Wit.ai response matcher: validates intents/entities and fires formatted UnityEvents.
    /// Extends Meta's WitIntentMatcher with optional entity-only matching (no intent required).
    /// </summary>
    [AddComponentMenu("Wit.ai/Response Matchers/Response Matcher")]
    public class MyWitResponseMatcher : WitIntentMatcher
    {
        [Header("Intent Matching")]
        [Tooltip("If enabled, the configured intent must be present. If disabled, entity-only responses are allowed.")]
        [SerializeField] private bool requireIntentMatch;

        [FormerlySerializedAs("valuePaths")]
        [Header("Value Matching")]
#if UNITY_2021_3_2 || UNITY_2021_3_3 || UNITY_2021_3_4 || UNITY_2021_3_5
        [NonReorderable]
#endif
        [SerializeField] public ValuePathMatcher[] valueMatchers;

        [Header("Output")]
#if UNITY_2021_3_2 || UNITY_2021_3_3 || UNITY_2021_3_4 || UNITY_2021_3_5
        [NonReorderable]
#endif
        [SerializeField] private FormattedValueEvents[] formattedValueEvents;
        [SerializeField] private MultiValueEvent onMultiValueEvent = new MultiValueEvent();

        [TooltipBox("Triggered if the matching conditions did not match. The parameter will be the transcription that was received. This will only trigger if there were values for intents or entities, but those values didn't match this matcher.")]
        [SerializeField] private StringEvent onDidNotMatch = new StringEvent();

        [TooltipBox("Triggered if a request was checked and no intents were found. This will still trigger if entities match and only applies to intents. The parameter will be the transcription.")]
        [SerializeField] private StringEvent onOutOfDomain = new StringEvent();

        private static Regex valueRegex = new Regex(Regex.Escape("{value}"), RegexOptions.Compiled);

        // Handle validation
        protected override string OnValidateResponse(WitResponseNode response, bool isEarlyResponse)
        {
            if (response == null)
            {
                return "No response";
            }

            var intents = response.GetIntents();
            bool hasIntents = intents != null && intents.Length > 0;
            bool hasEntities = response.EntityCount() > 0;

            Debug.Log($"Validating response. HasIntents: {hasIntents}, HasEntities: {hasEntities}, RequireIntentMatch: {requireIntentMatch}");

            if (!hasIntents && !hasEntities)
            {
                return "No intents or entities found";
            }

            if (requireIntentMatch)
            {
                if (!hasIntents)
                {
                    return "No intents found";
                }

                bool matchedIntent = false;
                for (int i = 0; i < intents.Length; i++)
                {
                    var intentData = intents[i];
                    if (!string.Equals(intent, intentData.name, StringComparison.CurrentCultureIgnoreCase))
                    {
                        continue;
                    }

                    if (intentData.confidence < confidenceThreshold)
                    {
                        return $"Required intent '{intent}' confidence too low: {intentData.confidence:0.000}\\nRequired: {confidenceThreshold:0.000}";
                    }

                    matchedIntent = true;
                    break;
                }

                if (!matchedIntent)
                {
                    return $"Missing required intent '{intent}'";
                }
            }

            // Only check value matches on early
            if (isEarlyResponse && !ValueMatches(response))
            {
                return "No value matches";
            }
            // Success
            return string.Empty;
        }
        // Ignore for mismatched intent
        protected override void OnResponseInvalid(WitResponseNode response, string error)
        {
            bool hasIntents = response.GetIntents().Length > 0;
            bool hasEntities = response.EntityCount() > 0;

            if (hasIntents || hasEntities)
            {
                onDidNotMatch?.Invoke(response.GetTranscription());
            }

            if (!hasIntents && !hasEntities)
            {
                onOutOfDomain?.Invoke(response.GetTranscription());
            }
        }
        // Handle valid callback
        protected override void OnResponseSuccess(WitResponseNode response)
        {
            // Check value matches
            if (ValueMatches(response))
            {
                for (int j = 0; j < formattedValueEvents.Length; j++)
                {
                    var formatEvent = formattedValueEvents[j];
                    var result = formatEvent.format;
                    for (int i = 0; i < valueMatchers.Length; i++)
                    {
                        var reference = valueMatchers[i].Reference;
                        var value = reference.GetStringValue(response);
                        if (!string.IsNullOrEmpty(formatEvent.format))
                        {
                            if (!string.IsNullOrEmpty(value))
                            {
                                result = valueRegex.Replace(result, value, 1);
                                result = result.Replace("{" + i + "}", value);
                            }
                            else if (result.Contains("{" + i + "}"))
                            {
                                result = "";
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(result))
                    {
                        formatEvent.onFormattedValueEvent?.Invoke(result);
                    }
                }
            }
            else
            {
                onDidNotMatch?.Invoke(response.GetTranscription());
            }

            // Get all values & perform multi value event
            List<string> values = new List<string>();
            foreach (var matcher in valueMatchers)
            {
                // Add value
                var value = matcher.Reference.GetStringValue(response);
                values.Add(value);

                // Refresh confidence
                if (matcher.ConfidenceReference != null)
                {
                    float confidenceValue = ValueMatches(response, matcher)
                        ? matcher.ConfidenceReference.GetFloatValue(response)
                        : 0f;
                    RefreshConfidenceRange(confidenceValue, matcher.confidenceRanges, matcher.allowConfidenceOverlap);
                }
            }
            onMultiValueEvent.Invoke(values.ToArray());
        }

        private bool ValueMatches(WitResponseNode response)
        {
            bool matches = true;
            for (int i = 0; i < valueMatchers.Length && matches; i++)
            {
                matches &= ValueMatches(response, valueMatchers[i]);
            }
            return matches;
        }

        private bool ValueMatches(WitResponseNode response, ValuePathMatcher matcher)
        {
            var value = matcher.Reference.GetStringValue(response);
            bool result = !matcher.contentRequired || !string.IsNullOrEmpty(value);
            switch (matcher.matchMethod)
            {
                case MatchMethod.RegularExpression:
                    result &= Regex.Match(value, matcher.matchValue).Success;
                    break;
                case MatchMethod.Text:
                    result &= value == matcher.matchValue;
                    break;
                case MatchMethod.IntegerComparison:
                    result &= CompareInt(value, matcher);
                    break;
                case MatchMethod.FloatComparison:
                    result &= CompareFloat(value, matcher);
                    break;
                case MatchMethod.DoubleComparison:
                    result &= CompareDouble(value, matcher);
                    break;
            }
            return result;
        }

        private bool CompareDouble(string value, ValuePathMatcher matcher)
        {

            // This one is freeform based on the input so we will retrun false if it is not parsable
            if (!double.TryParse(value, out double dValue)) return false;

            // We will throw an exception if match value is not a numeric value. This is a developer
            // error.
            double dMatchValue = double.Parse(matcher.matchValue);

            switch (matcher.comparisonMethod)
            {
                case ComparisonMethod.Equals:
                    return Math.Abs(dValue - dMatchValue) < matcher.floatingPointComparisonTolerance;
                case ComparisonMethod.NotEquals:
                    return Math.Abs(dValue - dMatchValue) > matcher.floatingPointComparisonTolerance;
                case ComparisonMethod.Greater:
                    return dValue > dMatchValue;
                case ComparisonMethod.Less:
                    return dValue < dMatchValue;
                case ComparisonMethod.GreaterThanOrEqualTo:
                    return dValue >= dMatchValue;
                case ComparisonMethod.LessThanOrEqualTo:
                    return dValue <= dMatchValue;
            }

            return false;
        }

        private bool CompareFloat(string value, ValuePathMatcher matcher)
        {

            // This one is freeform based on the input so we will retrun false if it is not parsable
            if (!float.TryParse(value, out float dValue)) return false;

            // We will throw an exception if match value is not a numeric value. This is a developer
            // error.
            float dMatchValue = float.Parse(matcher.matchValue);

            switch (matcher.comparisonMethod)
            {
                case ComparisonMethod.Equals:
                    return Math.Abs(dValue - dMatchValue) <
                           matcher.floatingPointComparisonTolerance;
                case ComparisonMethod.NotEquals:
                    return Math.Abs(dValue - dMatchValue) >
                           matcher.floatingPointComparisonTolerance;
                case ComparisonMethod.Greater:
                    return dValue > dMatchValue;
                case ComparisonMethod.Less:
                    return dValue < dMatchValue;
                case ComparisonMethod.GreaterThanOrEqualTo:
                    return dValue >= dMatchValue;
                case ComparisonMethod.LessThanOrEqualTo:
                    return dValue <= dMatchValue;
            }

            return false;
        }

        private bool CompareInt(string value, ValuePathMatcher matcher)
        {

            // This one is freeform based on the input so we will retrun false if it is not parsable
            if (!int.TryParse(value, out int dValue)) return false;

            // We will throw an exception if match value is not a numeric value. This is a developer
            // error.
            int dMatchValue = int.Parse(matcher.matchValue);

            switch (matcher.comparisonMethod)
            {
                case ComparisonMethod.Equals:
                    return dValue == dMatchValue;
                case ComparisonMethod.NotEquals:
                    return dValue != dMatchValue;
                case ComparisonMethod.Greater:
                    return dValue > dMatchValue;
                case ComparisonMethod.Less:
                    return dValue < dMatchValue;
                case ComparisonMethod.GreaterThanOrEqualTo:
                    return dValue >= dMatchValue;
                case ComparisonMethod.LessThanOrEqualTo:
                    return dValue <= dMatchValue;
            }

            return false;
        }
    }

}
