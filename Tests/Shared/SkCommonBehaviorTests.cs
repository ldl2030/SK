using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TestPlatform;
using TestPlatform.TestSequences;

internal static class SkCommonBehaviorTests
{
    public static void Validate(string boardType, IList steps)
    {
        foreach (object step in steps)
        {
            Type type = step.GetType();
            var definition = (TestPlanStepDefinition)type.GetProperty("PlanDefinition").GetValue(step);
            Require(definition != null && (string)type.GetProperty("Name").GetValue(step) == definition.Name,
                boardType + " grid and log labels must both use XML.");
        }
        using (var device = new SK441Device { SkipComInit = false, SimulationBoardType = boardType })
        {
            var daq = new RelayDaq();
            typeof(SK441Device).GetProperty("Daq").GetSetMethod(true).Invoke(device, new object[] { daq });
            Require(device.IsCompactLoggingEnabled() && !device.TargetBoard.ShouldLogSuccessfulTransmit(),
                boardType + " must use the shared logging policy.");
            Require(device.SetDaqRelaysAsync("@309:310", true, "initial").GetAwaiter().GetResult(), "Initial DAQ close failed.");
            Require(device.SetDaqRelaysAsync("310,@309,309", true, "duplicate").GetAwaiter().GetResult() && daq.Commands.Count == 1,
                "Equivalent ranges/order/duplicates must reuse command state.");
            Require(device.SetDaqRelaysAsync("@309:311", true, "overlap").GetAwaiter().GetResult() &&
                daq.Commands.Last() == "ROUT:CLOS (@311)", "Only changed channels should be sent.");
            daq.FailNext = true;
            Require(!device.SetDaqRelaysAsync("@309,310", false, "failed open").GetAwaiter().GetResult(), "Injected failure was hidden.");
            int writes = daq.Commands.Count;
            Require(device.SetDaqRelaysAsync("@309:310", true, "retry old state").GetAwaiter().GetResult() && daq.Commands.Count == writes + 1,
                "A failed command must invalidate old cached states.");
            writes = daq.Commands.Count;
            Require(!device.SetDaqRelaysAsync("@320:321", true, "invalid").GetAwaiter().GetResult() && daq.Commands.Count == writes,
                "Invalid DAQ relay configuration must fail before I/O.");
            Require(device.SetDaqRelaysAsync("@309:311", false, "cleanup").GetAwaiter().GetResult(), "DAQ cleanup failed.");
            typeof(SK441Device).GetMethod("ResetRelayCommandStates", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(device, null);
            writes = daq.Commands.Count;
            Require(device.SetDaqRelaysAsync("@309:311", false, "new initialization").GetAwaiter().GetResult() &&
                daq.Commands.Count == writes + 1, "Reinitialization must clear command state.");
            if (boardType.StartsWith("MPS", StringComparison.Ordinal))
            {
                daq.FailOnCommand = "ROUT:CLOS (@308)";
                Require(!device.ControlMpsRelaysAsync(new[] { 2, 3, 4 }, true).GetAwaiter().GetResult(),
                    "A failed group close must fail the step.");
                Require(!daq.Commands.Contains("ROUT:CLOS (@302)") &&
                    daq.Commands.Contains("ROUT:OPEN (@305,306)") && daq.Commands.Contains("ROUT:OPEN (@308)"),
                    "Group failure must stop later closes and isolate earlier/unknown contacts.");
            }
        }
        if (boardType.StartsWith("MPS", StringComparison.Ordinal))
        {
            using (var device = new SK441Device { SkipComInit = true, SimulationBoardType = boardType })
            {
                Require(device.ControlRelayAsync(2, false).GetAwaiter().GetResult(), "Station-3 Y2 preparation failed.");
                int count = device.GetPowerSafetyTrace().Length;
                Require(device.ControlMpsRelayAsync(2, "@305,306", false).GetAwaiter().GetResult() &&
                    device.GetPowerSafetyTrace().Length == count + 1,
                    "Station-3 Y2 must not suppress a logical DAQ Relay_2 operation.");
                Require(device.ControlRelayAsync(2, true).GetAwaiter().GetResult(), "Station-3 Y2 close failed.");
                count = device.GetPowerSafetyTrace().Length;
                Require(device.ControlMpsRelayAsync(2, "@305,306", false).GetAwaiter().GetResult() &&
                    device.GetPowerSafetyTrace().Length == count,
                    "Station-3 Y2 must not invalidate logical Relay_2 state.");
                Require(device.SetDaqRelaysAsync("@305,306", true, "direct change").GetAwaiter().GetResult(), "Direct DAQ change failed.");
                count = device.GetPowerSafetyTrace().Length;
                Require(device.ControlMpsRelayAsync(2, "@305,306", false).GetAwaiter().GetResult() &&
                    device.GetPowerSafetyTrace().Length == count + 1,
                    "Direct physical DAQ commands must update the logical state.");
                Require(!device.ControlMpsRelayAsync(2, null, true).GetAwaiter().GetResult() &&
                    !device.ControlMpsRelayAsync(2, "@302", true).GetAwaiter().GetResult(),
                    "Missing/wrong logical mapping must fail without station-3 fallback.");
                Require(device.SetVbattGeneratorAsync(0, 0.1f, 300, true).GetAwaiter().GetResult(), "VBATT setup failed.");
                Require(!device.SetDaqRelaysAsync("@304", true, "unsafe direct close").GetAwaiter().GetResult(),
                    "Direct DAQ commands must preserve the MPS power interlock.");
                Require(device.SetVbattGeneratorAsync(0, 0, 300, false).GetAwaiter().GetResult(), "VBATT shutdown failed.");
            }
        }
        ValidateModbusEcho();
        Console.WriteLine("PASS " + boardType + " shared policy: XML labels, logging, DAQ reuse/failure invalidation, isolated relay states and strict Modbus echo.");
    }

    private static void ValidateModbusEcho()
    {
        Type controller = typeof(SK441Device).Assembly.GetType("TestPlatform.RelayController");
        MethodInfo crc = controller.GetMethod("CalculateCrc", BindingFlags.Static | BindingFlags.NonPublic);
        Func<byte[], byte[]> frame = data => data.Concat((byte[])crc.Invoke(null, new object[] { data })).ToArray();
        MethodInfo validate = controller.GetMethod("IsSuccessfulWriteResponse", BindingFlags.Static | BindingFlags.NonPublic);
        byte[] request = frame(new byte[] { 3, 6, 0, 0, 0, 1 });
        Func<byte[], bool> accepts = response => (bool)validate.Invoke(null, new object[] { response, request });
        Require(accepts(request), "Matching valid Modbus echo rejected.");
        Require(!accepts(frame(new byte[] { 3, 0x86, 2 })), "Modbus exception accepted as success.");
        Require(!accepts(frame(new byte[] { 3, 6, 0, 1, 0, 1 })), "Wrong relay echo accepted.");
        Require(!accepts(frame(new byte[] { 3, 6, 0, 0, 0, 0 })), "Wrong state echo accepted.");
        byte[] corrupt = (byte[])request.Clone();
        corrupt[7] ^= 1;
        Require(!accepts(corrupt) && !accepts(request.Take(7).ToArray()), "Corrupt/truncated Modbus echo accepted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RelayDaq : DaqMultimeter
    {
        public readonly List<string> Commands = new List<string>();
        public bool FailNext;
        public string FailOnCommand;
        protected override void SendCommand(string command)
        {
            Commands.Add(command);
            if (FailNext || command == FailOnCommand)
            {
                FailNext = false;
                FailOnCommand = null;
                throw new TimeoutException("Injected partial relay command.");
            }
        }
    }
}
