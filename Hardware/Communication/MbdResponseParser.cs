using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestPlatform
{
    /// <summary>
    /// Parses MBD terminal replies without confusing the three-digit register
    /// identifier (for example 055) with the value after '='.
    /// </summary>
    public static class MbdResponseParser
    {
        private static readonly Regex RegisterLineRegex = new Regex(
            @"(?:^|[\r\n\0])\s*(?:R\s*)?(?<register>\d{1,3})\s*=\s*(?<payload>[^\r\n\0]*)",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase);

        private static readonly Regex NumberRegex = new Regex(
            @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private sealed class ErrorPattern
        {
            public string Name { get; set; }
            public Regex Regex { get; set; }
        }

        private static readonly ErrorPattern[] ErrorPatterns =
        {
            NewErrorPattern("RANGE ERROR", @"\bRANGE\s*[:\-]?\s*(?:ERROR|ERR)\b"),
            NewErrorPattern("COMMAND ERROR", @"\b(?:COMMAND|CMD)\s*[:\-]?\s*(?:ERROR|ERR)\b"),
            NewErrorPattern("INVALID COMMAND", @"\bINVALID\s+(?:COMMAND|CMD)\b"),
            NewErrorPattern("UNKNOWN COMMAND", @"\b(?:UNKNOWN|UNRECOGNIZED)\s+(?:COMMAND|CMD)\b"),
            NewErrorPattern("SYNTAX ERROR", @"\bSYNTAX\s*[:\-]?\s*(?:ERROR|ERR)\b"),
            NewErrorPattern("WRITE ERROR", @"\bWRITE\s*[:\-]?\s*(?:ERROR|ERR)\b"),
            NewErrorPattern("READ ONLY ERROR", @"\bREAD\s+ONLY\s+ERROR\b"),
            NewErrorPattern("DIAGNOSTIC MODE IS OFF", @"\bDIAGNOSTIC\s+MODE\s+IS\s+OFF\b"),
            NewErrorPattern("FAILED", @"\b(?:FAILED|FAIL)\b")
        };

        public static double ParseScalar(string command, string response)
        {
            Match line = GetRegisterLine(command, response);
            Match number = NumberRegex.Match(CleanPayload(line.Groups["payload"].Value));
            if (!number.Success)
            {
                throw new InvalidOperationException(
                    "MBD response contains no scalar value for " + command + ": " + response);
            }

            return double.Parse(number.Value, CultureInfo.InvariantCulture);
        }

        public static string ParsePayloadText(string command, string response)
        {
            Match line = GetRegisterLine(command, response);
            string payload = CleanPayload(line.Groups["payload"].Value);
            if (payload.Length == 0)
            {
                throw new InvalidOperationException(
                    "MBD response contains no payload value for " + command + ": " + response);
            }

            return payload;
        }

        private static string CleanPayload(string payload)
        {
            payload = payload ?? string.Empty;
            int metadataIndex = payload.IndexOf(';');
            if (metadataIndex >= 0)
                payload = payload.Substring(0, metadataIndex);

            payload = Regex.Replace(
                payload,
                @"\s*\|\s*Ready\.?\s*$",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return payload.Trim().Trim('\0');
        }

        public static int[] ParseIntegerVector(
            string command,
            string response,
            int expectedCount)
        {
            Match line = GetRegisterLine(command, response);
            string[] fields = CleanPayload(line.Groups["payload"].Value)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var values = new List<int>();
            foreach (string field in fields)
            {
                Match number = NumberRegex.Match(field.Trim());
                int value;
                if (!number.Success ||
                    !int.TryParse(
                        number.Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out value))
                {
                    throw new InvalidOperationException(
                        "MBD response contains an invalid integer vector for " + command + ": " + response);
                }
                values.Add(value);
            }

            if (expectedCount > 0 && values.Count != expectedCount)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "MBD response for {0} expected {1} values but received {2}: {3}",
                    command,
                    expectedCount,
                    values.Count,
                    response));
            }

            return values.ToArray();
        }

        public static bool IsSuccessfulCommandResponse(string response, out string error)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                error = "MBD command returned no response.";
                return false;
            }

            foreach (ErrorPattern pattern in ErrorPatterns)
            {
                if (pattern.Regex.IsMatch(response))
                {
                    error = pattern.Name;
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Validates a non-query command with command-specific terminal semantics.
        /// EXIT is idempotent: when the board is already at the MBD root terminal it
        /// replies COMMAND ERROR followed by Ready.; that still proves the requested
        /// terminal state and the serial exchange both completed successfully.
        /// </summary>
        public static bool IsSuccessfulCommandResponse(
            string command,
            string response,
            out string error)
        {
            if (IsSuccessfulCommandResponse(response, out error))
                return true;

            if (IsAcceptedExitNoOpResponse(command, response, error))
            {
                error = string.Empty;
                return true;
            }

            return false;
        }

        public static bool IsAcceptedExitNoOpResponse(
            string command,
            string response,
            string responseError = "COMMAND ERROR")
        {
            return string.Equals(
                       (command ?? string.Empty).Trim(),
                       "EXIT",
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       responseError,
                       "COMMAND ERROR",
                       StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(response) &&
                   ContainsResponseError(response, "COMMAND ERROR") &&
                   ErrorPatterns
                       .Where(pattern => !string.Equals(
                           pattern.Name,
                           "COMMAND ERROR",
                           StringComparison.OrdinalIgnoreCase))
                       .All(pattern => !pattern.Regex.IsMatch(response)) &&
                   TargetBoardComm.HasTerminalPrompt(response);
        }

        private static Match GetRegisterLine(string command, string response)
        {
            string error;
            if (!IsSuccessfulCommandResponse(response, out error))
            {
                throw new InvalidOperationException(
                    "MBD response error for " + command + ": " + error + ". Response: " + response);
            }

            MatchCollection lines = RegisterLineRegex.Matches(response ?? string.Empty);
            if (lines.Count == 0)
            {
                throw new InvalidOperationException(
                    "MBD response contains no register assignment for " + command + ": " + response);
            }

            int expectedRegister;
            if (!TryGetRequestedRegister(command, out expectedRegister))
                return lines[0];

            foreach (Match candidate in lines)
            {
                int actualRegister;
                if (int.TryParse(
                        candidate.Groups["register"].Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out actualRegister) &&
                    actualRegister == expectedRegister)
                {
                    return candidate;
                }
            }

            string receivedRegisters = string.Join(
                ", ",
                lines.Cast<Match>().Select(item => item.Groups["register"].Value));
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture,
                "MBD response register mismatch: requested {0}, received {1}. Response: {2}",
                command,
                receivedRegisters,
                response));
        }

        private static ErrorPattern NewErrorPattern(string name, string pattern)
        {
            return new ErrorPattern
            {
                Name = name,
                Regex = new Regex(
                    pattern,
                    RegexOptions.Compiled |
                    RegexOptions.CultureInvariant |
                    RegexOptions.IgnoreCase)
            };
        }

        private static bool ContainsResponseError(string response, string errorName)
        {
            ErrorPattern pattern = ErrorPatterns.FirstOrDefault(item => string.Equals(
                item.Name,
                errorName,
                StringComparison.OrdinalIgnoreCase));
            return pattern != null && pattern.Regex.IsMatch(response ?? string.Empty);
        }

        private static bool TryGetRequestedRegister(string command, out int register)
        {
            register = 0;
            Match match = Regex.Match(
                command ?? string.Empty,
                @"^\s*[Rr](?<register>\d+)\s*$",
                RegexOptions.CultureInvariant);
            return match.Success &&
                   int.TryParse(
                       match.Groups["register"].Value,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out register);
        }
    }
}
