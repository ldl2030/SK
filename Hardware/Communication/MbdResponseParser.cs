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
            @"(?m)^\s*(?<register>\d{3})\s*=\s*(?<payload>[^\r\n]*)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex NumberRegex = new Regex(
            @"[-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly string[] ErrorMarkers =
        {
            "RANGE ERROR",
            "COMMAND ERROR",
            "INVALID COMMAND",
            "SYNTAX ERROR",
            "WRITE ERROR",
            "FAILED",
            "FAIL"
        };

        public static double ParseScalar(string command, string response)
        {
            Match line = GetRegisterLine(command, response);
            Match number = NumberRegex.Match(line.Groups["payload"].Value);
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
            string payload = line.Groups["payload"].Value;
            int metadataIndex = payload.IndexOf(';');
            if (metadataIndex >= 0)
                payload = payload.Substring(0, metadataIndex);

            payload = payload.Trim();
            if (payload.Length == 0)
            {
                throw new InvalidOperationException(
                    "MBD response contains no payload value for " + command + ": " + response);
            }

            return payload;
        }

        public static int[] ParseIntegerVector(
            string command,
            string response,
            int expectedCount)
        {
            Match line = GetRegisterLine(command, response);
            string[] fields = line.Groups["payload"].Value
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

            foreach (string marker in ErrorMarkers)
            {
                if (response.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    error = marker;
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
                   response.IndexOf(
                       "COMMAND ERROR",
                       StringComparison.OrdinalIgnoreCase) >= 0 &&
                   ErrorMarkers
                       .Where(marker => !string.Equals(
                           marker,
                           "COMMAND ERROR",
                           StringComparison.OrdinalIgnoreCase))
                       .All(marker => response.IndexOf(
                           marker,
                           StringComparison.OrdinalIgnoreCase) < 0) &&
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

            Match line = RegisterLineRegex.Match(response);
            if (!line.Success)
            {
                throw new InvalidOperationException(
                    "MBD response contains no register assignment for " + command + ": " + response);
            }

            int expectedRegister;
            int actualRegister;
            if (TryGetRequestedRegister(command, out expectedRegister) &&
                int.TryParse(
                    line.Groups["register"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out actualRegister) &&
                actualRegister != expectedRegister)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "MBD response register mismatch: requested {0}, received {1}. Response: {2}",
                    command,
                    line.Groups["register"].Value,
                    response));
            }

            return line;
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
