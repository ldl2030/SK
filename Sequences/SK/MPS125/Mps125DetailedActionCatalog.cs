using System.Collections.Generic;
using System.Globalization;

namespace TestPlatform.TestSequences
{
    /// <summary>
    /// Operator-visible action catalogue for MPS UP125 Rev.3. Commands, relay
    /// changes, source settings, waits, measurements, calculations and cleanup
    /// are intentionally separate rows. Limits, units and delays live in XML.
    /// </summary>
    public static partial class Mps125ActionCatalog
    {
        public static IReadOnlyList<Mps125ActionDefinition> Create()
        {
            var items = new List<Mps125ActionDefinition>();
            AddChapter06(items);
            AddChapter07(items);
            AddChapter08(items);
            AddChapter09(items);
            AddChapter10(items);
            AddChapter11(items);
            AddChapter12(items);
            AddChapter13(items);
            AddChapter14(items);
            AddChapter15(items);
            AddChapter16(items);
            AddChapter17(items);
            AddCleanup(items);

            Require(items, "H09", "H03");
            Require(items, "J05", "J01");
            Require(items, "J06", "J02");
            Require(items, "J07", "J03");
            Require(items, "J08", "J04");
            AssignStableIds(items);
            return items;
        }

        private static void AddChapter06(List<Mps125ActionDefinition> x)
        {
            A(x, 6, "6.1 检查34901A/34903A/34907A、VBUS及VBATT电源在线", Mps125ActionKind.CheckInstruments);
            A(x, 6, "6.2 关闭VBUS/VBATT及其它仪器输出", Mps125ActionKind.AllOutputsOff);
            Relay(x, 6, "6.3 断开DAQ301-306功能继电器", Mps125StationMap.AllControlledRelays, false);
        }

        private static void AddChapter07(List<Mps125ActionDefinition> x)
        {
            Vbus(x, 7, "7.1 设置VBUS 137.5V/2A OVP300", 137.5, 2, 300, false);
            Relay(x, 7, "7.2 闭合Relay_3(DAQ301) withdrawal接点", "3", true);
            Vbus(x, 7, "7.1 功率路径闭合后打开VBUS输出", 137.5, 2, 300, true);
            Daq(x, 7, "B01", "7.3.I 测量+5V DAQ101", Mps125StationMap.FiveVolt);
            Daq(x, 7, "B02", "7.3.II 测量+VCC DAQ102", Mps125StationMap.Vcc);
            Daq(x, 7, "B03", "7.3.III 测量-VEE DAQ103", Mps125StationMap.Vee);
            Daq(x, 7, "B04", "7.3.IV 测量+5VI DAQ119", Mps125StationMap.FiveVoltIsolated);
            Daq(x, 7, "B05", "7.3.V 测量VPWM DAQ114", Mps125StationMap.Vpwm);
            Shunt(x, 7, "B06", "7.3.VI 通过1Ω分流测量VBUS吸收电流 DAQ117", Mps125StationMap.VbusShunt, Mps125StationMap.VbusShuntOhms);
            Daq(x, 7, "B07", "7.3.VII 测量P+ DAQ113", Mps125StationMap.PPlus);
            Vbus(x, 7, "7.4 关闭VBUS输出", 137.5, 2, 300, false);
            A(x, 7, "7.5 关闭本章仪器串口会话", Mps125ActionKind.DocumentedBoundary);
        }

        private static void AddChapter08(List<Mps125ActionDefinition> x)
        {
            Vbus(x, 8, "8.1/3 设置VBUS起始点5V/0.5A OVP300", 5, 0.5, 300, false);
            Vbus(x, 8, "8.4 打开VBUS输出", 5, 0.5, 300, true);
            Wait(x, 8, "8.5 等待2000ms");
            Result(x, 8, "C01", "8.6-8.8 逐步升高VBUS并记录+5V超过4.8V时的点火电压", Mps125ActionKind.IgnitionRamp, "5,80,1", Mps125StationMap.FiveVolt);
            Vbus(x, 8, "8.9 关闭VBUS输出", 0, 0, 300, false);
            A(x, 8, "8.10 关闭串口会话", Mps125ActionKind.CloseMbdCommunication);
        }

        private static void AddChapter09(List<Mps125ActionDefinition> x)
        {
            Vbus(x, 9, "9.1 设置VBUS 137.5V/2A OVP300", 137.5, 2, 300, false);
            Vbus(x, 9, "9.1 打开VBUS输出", 137.5, 2, 300, true);
            Daq(x, 9, "D01", "9.2.I 测量COM_USB DAQ111", Mps125StationMap.ComUsb);
            Daq(x, 9, "D02", "9.2.II 测量3V3 DAQ120", Mps125StationMap.ThreeVoltThree);
            Wait(x, 9, "9.3 等待3000ms");
            A(x, 9, "9.4 初始化USB-B目标板串口115200 8N1", Mps125ActionKind.CheckMbdCommunication);
            Cmd(x, 9, "9.5 发送EXIT进入MCU终端", "EXIT");
            Result(x, 9, "D03", "9.6-9.7 发送R0并保存固件版本", Mps125ActionKind.FirmwareVersion, "R0");
            Vbus(x, 9, "9.8 关闭VBUS输出", 137.5, 2, 300, false);
            A(x, 9, "9.9 关闭目标板串口通信", Mps125ActionKind.CloseMbdCommunication);
        }

        private static void AddChapter10(List<Mps125ActionDefinition> x)
        {
            Relay(x, 10, "10.1 断开Relay_2/2BIS(DAQ305/306)使+5VEXT与GND_EXT浮空", "2", false);
            Vbus(x, 10, "10.2 设置VBUS 137.5V/2A OVP300", 137.5, 2, 300, false);
            Vbus(x, 10, "10.2 打开VBUS输出", 137.5, 2, 300, true);
            A(x, 10, "10.3 文档标记为Step removed", Mps125ActionKind.DocumentedBoundary);
            Cmd(x, 10, "10.4 发送EXIT", "EXIT");
            Mbd(x, 10, "E01", "10.5 读取R78 st_remoto（外部电源断开）", "R78");
            Relay(x, 10, "10.6 闭合Relay_2/2BIS(DAQ305/306)连接外部5V和GND", "2", true);
            Wait(x, 10, "10.7 等待200ms");
            Mbd(x, 10, "E02", "10.8 读取R78 st_remoto（外部电源接通）", "R78");
            Relay(x, 10, "10.9 再次断开Relay_2/2BIS(DAQ305/306)", "2", false);
        }

        private static void AddChapter11(List<Mps125ActionDefinition> x)
        {
            Cmd(x, 11, "11.1 发送EXIT", "EXIT");
            Cmd(x, 11, "11.2 发送DIAG ON", "DIAG ON");
            Cmd(x, 11, "11.3 设置主开关1 PWM W101=700", "W101=700");
            Cmd(x, 11, "11.4 设置主开关2 PWM W295=700", "W295=700");
            Wait(x, 11, "11.5 等待500ms");
            Mbd(x, 11, "F01", "11.6 读取R79 withdrawal_status", "R79");
            Daq(x, 11, "F02", "11.7 测量OPEN_PREC_RL DAQ106", Mps125StationMap.OpenPrec);
            DaqAvg(x, 11, "F03", "11.8 U20.7 DAQ109采样16次/100ms取平均", Mps125StationMap.U20Pin7, 16, 100);
            DaqAvg(x, 11, "F04", "11.9 U20.5 DAQ110采样16次/100ms取平均", Mps125StationMap.U20Pin5, 16, 100);
            Relay(x, 11, "11.10 断开Relay_3(DAQ301) withdrawal接点", "3", false);
            Wait(x, 11, "11.11 等待500ms");
            Mbd(x, 11, "F05", "11.12 读取R79 withdrawal_state", "R79");
            Daq(x, 11, "F06", "11.13 测量OPEN_PREC_RL DAQ106", Mps125StationMap.OpenPrec);
            DaqAvg(x, 11, "F07", "11.14 U20.7 DAQ109采样16次/100ms取平均", Mps125StationMap.U20Pin7, 16, 100);
            DaqAvg(x, 11, "F08", "11.15 U20.5 DAQ110采样16次/100ms取平均", Mps125StationMap.U20Pin5, 16, 100);
            Cmd(x, 11, "11.16 关闭主开关1 W101=0", "W101=0");
            Cmd(x, 11, "11.17 关闭主开关2 W295=0", "W295=0");
            Relay(x, 11, "11.18 闭合Relay_4(DAQ302)强制CONTACT_80V=0", "4", true);
            Wait(x, 11, "11.19 等待500ms");
            Mbd(x, 11, "F09", "11.20 读取R357 80V_PRESENCE低态", "R357");
            Relay(x, 11, "11.21 断开Relay_4(DAQ302)强制CONTACT_80V=1", "4", false);
            Wait(x, 11, "11.22 等待500ms");
            Mbd(x, 11, "F10", "11.23 读取R357 80V_PRESENCE高态", "R357");
            Relay(x, 11, "11.24 恢复闭合Relay_3(DAQ301) withdrawal接点", "3", true);
        }

        private static void AddChapter12(List<Mps125ActionDefinition> x)
        {
            Cmd(x, 12, "12.1 发送EXIT", "EXIT");
            Cmd(x, 12, "12.2 发送DIAG ON", "DIAG ON");
            Cmd(x, 12, "12.3 闭合报警继电器 W89=1", "W89=1");
            Wait(x, 12, "12.4 等待500ms");
            Resistance(x, 12, "G01", "12.5 测量LSI.COM触点闭合电阻 DAQ118", Mps125StationMap.AlarmContact);
            Cmd(x, 12, "12.6 断开报警继电器 W89=0", "W89=0");
            Wait(x, 12, "12.7 等待500ms");
            Resistance(x, 12, "G02", "12.8 测量LSI.COM触点断开电阻 DAQ118", Mps125StationMap.AlarmContact);
        }

        private static void AddChapter13(List<Mps125ActionDefinition> x)
        {
            Cmd(x, 13, "13.1 发送EXIT", "EXIT");
            Cmd(x, 13, "13.2 发送DIAG ON", "DIAG ON");
            DaqAvg(x, 13, "H01", "13.3 U20.7静态采样16次/100ms", Mps125StationMap.U20Pin7, 16, 100);
            DaqAvg(x, 13, "H02", "13.4 U20.5静态采样16次/100ms", Mps125StationMap.U20Pin5, 16, 100);
            Shunt(x, 13, "H03", "13.5 通过1Ω分流测量MPS静态电流", Mps125StationMap.VbusShunt, Mps125StationMap.VbusShuntOhms);
            Cmd(x, 13, "13.6 设置pmos_sw_mcu W96=0", "W96=0");
            Cmd(x, 13, "13.7 设置主开关1 PWM W101=700", "W101=700");
            Cmd(x, 13, "13.8 设置主开关2 PWM W295=700", "W295=700");
            Wait(x, 13, "13.9 等待200ms");
            DaqAvg(x, 13, "H04", "13.10 U20.7高驱动采样16次/100ms", Mps125StationMap.U20Pin7, 16, 100);
            DaqAvg(x, 13, "H05", "13.11 U20.5高驱动采样16次/100ms", Mps125StationMap.U20Pin5, 16, 100);
            Shunt(x, 13, "H06", "13.12 测量高驱动MPS电流", Mps125StationMap.VbusShunt, Mps125StationMap.VbusShuntOhms);
            Cmd(x, 13, "13.13 设置主开关1 PWM W101=200", "W101=200");
            Cmd(x, 13, "13.14 设置主开关2 PWM W295=200", "W295=200");
            DaqAvg(x, 13, "H07", "13.15 U20.7低驱动采样16次/100ms", Mps125StationMap.U20Pin7, 16, 100);
            DaqAvg(x, 13, "H08", "13.16 U20.5低驱动采样16次/100ms", Mps125StationMap.U20Pin5, 16, 100);
            Shunt(x, 13, "H09", "13.17 测量低驱动电流并与H03比较", Mps125StationMap.VbusShunt, Mps125StationMap.VbusShuntOhms);
            Wait(x, 13, "13.18 等待200ms");
            Cmd(x, 13, "13.19 关闭主开关1 W101=0", "W101=0");
            Cmd(x, 13, "13.20 关闭主开关2 W295=0", "W295=0");
            Cmd(x, 13, "13.21 关闭pmos_sw_mcu W96=5", "W96=5");
            Wait(x, 13, "13.22 等待500ms");
            DaqAvg(x, 13, "H10", "13.23 U20.7关闭后采样16次/100ms", Mps125StationMap.U20Pin7, 16, 100);
            DaqAvg(x, 13, "H11", "13.24 U20.5关闭后采样16次/100ms", Mps125StationMap.U20Pin5, 16, 100);
            Shunt(x, 13, "H12", "13.25 测量关闭后MPS电流", Mps125StationMap.VbusShunt, Mps125StationMap.VbusShuntOhms);
            Cmd(x, 13, "13.26 安全确认主开关1 W101=0", "W101=0");
        }

        private static void AddChapter14(List<Mps125ActionDefinition> x)
        {
            Cmd(x, 14, "14.1 发送EXIT", "EXIT");
            Cmd(x, 14, "14.2 发送DIAG ON", "DIAG ON");
            Cmd(x, 14, "14.3 设置pmos_sw_mcu W96=0", "W96=0");
            Cmd(x, 14, "14.4 设置主开关1 W101=700", "W101=700");
            Cmd(x, 14, "14.5 设置主开关2 W295=700", "W295=700");
            Wait(x, 14, "14.6 等待200ms");
            Cmd(x, 14, "14.7 设置主开关1 W101=200", "W101=200");
            Cmd(x, 14, "14.8 设置主开关2 W295=200", "W295=200");
            Wait(x, 14, "14.9 等待600ms");
            DaqAvg(x, 14, "I01", "14.10 U20.7采样16次/100ms", Mps125StationMap.U20Pin7, 16, 100);
            DaqAvg(x, 14, "I02", "14.11 U20.5采样16次/100ms", Mps125StationMap.U20Pin5, 16, 100);
            Mbd(x, 14, "I03", "14.12 读取R81 st_westngh", "R81");
            Cmd(x, 14, "14.13 置位west_cmd W95=1", "W95=1");
            Wait(x, 14, "14.14 等待600ms");
            DaqAvg(x, 14, "I04", "14.15 U20.7急停后采样16次/100ms", Mps125StationMap.U20Pin7, 16, 100);
            DaqAvg(x, 14, "I05", "14.16 U20.5急停后采样16次/100ms", Mps125StationMap.U20Pin5, 16, 100);
            Mbd(x, 14, "I06", "14.17 读取R81 mcu_west_stat", "R81");
            Cmd(x, 14, "14.18 关闭主开关1 W101=0", "W101=0");
            Cmd(x, 14, "14.19 关闭主开关2 W295=0", "W295=0");
            Cmd(x, 14, "14.20 清除west_cmd W95=0", "W95=0");
            Wait(x, 14, "14.21 等待200ms");
            Cmd(x, 14, "14.22 关闭pmos_sw_mcu W96=5", "W96=5");
            A(x, 14, "14.23 本章结束边界", Mps125ActionKind.DocumentedBoundary);
        }

        private static void AddChapter15(List<Mps125ActionDefinition> x)
        {
            Cmd(x, 15, "15.1 发送EXIT", "EXIT");
            Cmd(x, 15, "15.2 发送DIAG ON", "DIAG ON");
            Mbd(x, 15, "J01", "15.3 读取R69 adc3 vbus+ PA3", "R69");
            A(x, 15, "15.4 关闭VBUS、闭合Relay_5(DAQ303)后恢复137.5V输出", Mps125ActionKind.SafeVbusRelay, "5,137.5,2,300,1", true);
            Wait(x, 15, "15.5 等待500ms");
            Mbd(x, 15, "J02", "15.6 读取R68 VSensPrec PC1", "R68");
            Vbatt(x, 15, "15.7 设置VBATT 137.5V/0.1A OVP300", 137.5, 0.1, 300, false);
            Vbatt(x, 15, "15.7 打开VBATT输出", 137.5, 0.1, 300, true);
            Wait(x, 15, "15.8 等待5000ms");
            Cmd(x, 15, "15.9 闭合内部RL2 W94=1", "W94=1");
            Wait(x, 15, "15.10 等待500ms");
            Mbd(x, 15, "J03", "15.11 读取R80 adc4 vstring1+ PA4", "R80");
            Cmd(x, 15, "15.12 闭合内部RL1 W91=1", "W91=1");
            Wait(x, 15, "15.13 等待500ms");
            Mbd(x, 15, "J04", "15.14 读取R70 adc2 vstring2+ PA2", "R70");
            A(x, 15, "15.15 DAQ113独立测量P+参考电压", Mps125ActionKind.DaqVoltage, Mps125StationMap.VbusReference.ToString(CultureInfo.InvariantCulture));
            Result(x, 15, "J05", "15.16 计算Gain_VBUS=4096*VBUS/J01", Mps125ActionKind.ComputedGain, "J01", Mps125StationMap.VbusReference);
            Result(x, 15, "J06", "15.17 计算Gain_VPREC=4096*VBUS/J02", Mps125ActionKind.ComputedGain, "J02", Mps125StationMap.VbusReference);
            Result(x, 15, "J07", "15.18 计算Gain_VSTRING=4096*VBUS/J03", Mps125ActionKind.ComputedGain, "J03", Mps125StationMap.VbusReference);
            Result(x, 15, "J08", "15.19 计算Gain_VSTRING2=4096*VBUS/J04", Mps125ActionKind.ComputedGain, "J04", Mps125StationMap.VbusReference);
            Cmd(x, 15, "15.20 断开内部RL1 W91=0", "W91=0");
            Cmd(x, 15, "15.21 断开内部RL2 W94=0", "W94=0");
            A(x, 15, "15.22 关闭VBUS、断开Relay_5(DAQ303)后恢复137.5V输出", Mps125ActionKind.SafeVbusRelay, "5,137.5,2,300,1", false);
            Vbatt(x, 15, "15.23 关闭VBATT输出", 0, 0, 300, false);
        }

        private static void AddChapter16(List<Mps125ActionDefinition> x)
        {
            Cmd(x, 16, "16.1 发送EXIT", "EXIT");
            Cmd(x, 16, "16.2 发送DIAG ON", "DIAG ON");
            DaqAvg(x, 16, "K01", "16.3 MCU_ISOBATT基线测量约8秒 DAQ105", Mps125StationMap.McuIsoBatt, 16, 500);
            Vbatt(x, 16, "16.4 设置VBATT 0V/0.1A OVP150并输出", 0, 0.1, 150, true);
            A(x, 16, "16.5 在VBATT关闭时闭合Relay_6(DAQ304)后输出0V", Mps125ActionKind.SafeVbattConnect, "6");
            DaqAvg(x, 16, "K02", "16.6 MCU_ISOBATT@0V测量约8秒 DAQ105", Mps125StationMap.McuIsoBatt, 16, 500);
            Vbatt(x, 16, "16.7 设置VBATT 125V/0.1A OVP150并输出", 125, 0.1, 150, true);
            DaqAvg(x, 16, "K03", "16.8 MCU_ISOBATT@125V测量约8秒 DAQ105", Mps125StationMap.McuIsoBatt, 16, 500);
            A(x, 16, "16.9 先关闭VBATT再断开Relay_6(DAQ304) ISOBATT_INPUT", Mps125ActionKind.SafeVbattDisconnect, "6", false);
            Vbatt(x, 16, "16.10 确认VBATT保持关闭", 0, 0, 150, false);
        }

        private static void AddChapter17(List<Mps125ActionDefinition> x)
        {
            Cmd(x, 17, "17.1 发送EXIT", "EXIT");
            Cmd(x, 17, "17.2 发送DIAG ON", "DIAG ON");
            Cmd(x, 17, "17.3 进入SAFE终端 REMOTE 31", "REMOTE 31");
            Cmd(x, 17, "17.4 SAFE关闭risc_on W38=0", "W38=0");
            Cmd(x, 17, "17.5 返回MCU终端 EXIT", "EXIT");
            Cmd(x, 17, "17.6 发送DIAG ON", "DIAG ON");
            Cmd(x, 17, "17.7 关闭PWM1 W99=0", "W99=0");
            Cmd(x, 17, "17.8 关闭PWM2 W100=0", "W100=0");
            Cmd(x, 17, "17.9 打开MCU risc_on W92=1", "W92=1");
            Wait(x, 17, "17.10 等待200ms");
            Mbd(x, 17, "L01", "17.11 读取R71 ridstrneg基线", "R71");
            DaqAvg(x, 17, "L02", "17.12-13 FBK_1 DAQ107采样16次/100ms", Mps125StationMap.HeaterFeedback1, 16, 100);
            DaqAvg(x, 17, "L03", "17.12-14 FBK_2 DAQ108采样16次/100ms", Mps125StationMap.HeaterFeedback2, 16, 100);
            Shunt(x, 17, "L04", "17.15 测量加热器关闭时MPS电流", Mps125StationMap.VbusShunt, Mps125StationMap.VbusShuntOhms);
            Cmd(x, 17, "17.16 激活PWM1 W99=1000", "W99=1000");
            Wait(x, 17, "17.17 等待10000ms");
            Mbd(x, 17, "L05", "17.18 读取R71 ridstrneg（PWM1开启）", "R71");
            DaqAvg(x, 17, "L06", "17.19-20 FBK_1 100%状态采样16次/100ms", Mps125StationMap.HeaterFeedback1, 16, 100);
            Shunt(x, 17, "L07", "17.21 测量PWM1开启时MPS电流", Mps125StationMap.VbusShunt, Mps125StationMap.VbusShuntOhms);
            Cmd(x, 17, "17.22 关闭PWM1 W99=0", "W99=0");
            Wait(x, 17, "17.22 等待10000ms");
            DaqAvg(x, 17, "L08", "17.23 FBK_1恢复0%采样16次/100ms", Mps125StationMap.HeaterFeedback1, 16, 100);
            Wait(x, 17, "17.24 等待500ms");
            Cmd(x, 17, "17.25 激活PWM2 W100=1000", "W100=1000");
            Wait(x, 17, "17.26 等待10000ms");
            Mbd(x, 17, "L09", "17.27 读取R71 ridstrneg（PWM2开启）", "R71");
            DaqAvg(x, 17, "L10", "17.28-29 FBK_2 100%状态采样16次/100ms", Mps125StationMap.HeaterFeedback2, 16, 100);
            Shunt(x, 17, "L11", "17.30 测量PWM2开启时MPS电流", Mps125StationMap.VbusShunt, Mps125StationMap.VbusShuntOhms);
            Cmd(x, 17, "17.31 关闭两路加热器risc_on W92=0", "W92=0");
            Wait(x, 17, "17.31 等待10000ms");
            DaqAvg(x, 17, "L12", "17.32 FBK_2恢复0%采样16次/100ms", Mps125StationMap.HeaterFeedback2, 16, 100);
            Wait(x, 17, "17.33 等待1000ms");
            Mbd(x, 17, "L13", "17.34 读取R71 ridstrneg（risc_on关闭）", "R71");
            Cmd(x, 17, "17.35 确认PWM1关闭 W99=0", "W99=0");
            Cmd(x, 17, "17.36 确认PWM2关闭 W100=0", "W100=0");
            Wait(x, 17, "17.36 等待10000ms");
            DaqAvg(x, 17, "L14", "17.36-37 FBK_1最终采样16次/100ms", Mps125StationMap.HeaterFeedback1, 16, 100);
            DaqAvg(x, 17, "L15", "17.36-38 FBK_2最终采样16次/100ms", Mps125StationMap.HeaterFeedback2, 16, 100);
            Shunt(x, 17, "L16", "17.39 测量最终MPS电流", Mps125StationMap.VbusShunt, Mps125StationMap.VbusShuntOhms);
            Vbus(x, 17, "17.40 关闭VBUS输出", 0, 0, 300, false);
            Relay(x, 17, "17.42 断开Relay_3(DAQ301) withdrawal接点", "3", false);
            A(x, 17, "17.43 关闭目标板串口通信", Mps125ActionKind.CloseMbdCommunication);
        }

        private static void AddCleanup(List<Mps125ActionDefinition> x)
        {
            Cleanup(x, "发送主开关/加热器安全关闭命令", Mps125ActionKind.MbdCommand, "W101=0;W295=0;W95=0;W96=5;W99=0;W100=0;W92=0", true);
            Cleanup(x, "关闭VBUS输出", Mps125ActionKind.SetVbus, "0,0,300", false);
            Cleanup(x, "关闭VBATT输出", Mps125ActionKind.SetVbatt, "0,0,300", false);
            Cleanup(x, "断开DAQ301-306功能继电器", Mps125ActionKind.DaqRelays, Mps125StationMap.AllControlledRelays, false);
            Cleanup(x, "关闭所有仪器输出", Mps125ActionKind.AllOutputsOff, null, false);
            Cleanup(x, "关闭目标板通信并释放治具", Mps125ActionKind.Cleanup, null, false);
        }

        private static void A(List<Mps125ActionDefinition> x, int ch, string name, Mps125ActionKind kind, string arg = null, bool state = true)
        {
            x.Add(new Mps125ActionDefinition { Chapter = ch, Name = name, Kind = kind, Argument = arg, State = state });
        }

        private static void Result(List<Mps125ActionDefinition> x, int ch, string id, string name, Mps125ActionKind kind, string arg = null, int channel = 0, double scale = 0, int samples = 1, int interval = 0)
        {
            x.Add(new Mps125ActionDefinition { Chapter = ch, Name = id + " " + name, Kind = kind, Argument = arg, ResultId = id, Channel = channel, Scale = scale, SampleCount = samples, SampleIntervalMs = interval, State = true });
        }

        private static void Relay(List<Mps125ActionDefinition> x, int ch, string name, string relays, bool state) => A(x, ch, name, Mps125ActionKind.DaqRelays, relays, state);
        private static void Cmd(List<Mps125ActionDefinition> x, int ch, string name, string command) => A(x, ch, name, Mps125ActionKind.MbdCommand, command);
        private static void Wait(List<Mps125ActionDefinition> x, int ch, string name) => A(x, ch, name, Mps125ActionKind.Delay);
        private static void Vbus(List<Mps125ActionDefinition> x, int ch, string name, double v, double a, double ovp, bool state) => A(x, ch, name, Mps125ActionKind.SetVbus, Numbers(v, a, ovp), state);
        private static void Vbatt(List<Mps125ActionDefinition> x, int ch, string name, double v, double a, double ovp, bool state) => A(x, ch, name, Mps125ActionKind.SetVbatt, Numbers(v, a, ovp), state);
        private static void Mbd(List<Mps125ActionDefinition> x, int ch, string id, string name, string command) => Result(x, ch, id, name, Mps125ActionKind.MbdNumeric, command);
        private static void Daq(List<Mps125ActionDefinition> x, int ch, string id, string name, int channel) => Result(x, ch, id, name, Mps125ActionKind.DaqVoltage, null, channel);
        private static void DaqAvg(List<Mps125ActionDefinition> x, int ch, string id, string name, int channel, int samples, int interval) => Result(x, ch, id, name, Mps125ActionKind.DaqAverageVoltage, null, channel, 0, samples, interval);
        private static void Shunt(List<Mps125ActionDefinition> x, int ch, string id, string name, int channel, double ohms) => Result(x, ch, id, name, Mps125ActionKind.DaqShuntCurrent, null, channel, ohms);
        private static void Resistance(List<Mps125ActionDefinition> x, int ch, string id, string name, int channel) => Result(x, ch, id, name, Mps125ActionKind.DaqResistance, null, channel);

        private static void Cleanup(List<Mps125ActionDefinition> x, string name, Mps125ActionKind kind, string arg, bool state)
        {
            x.Add(new Mps125ActionDefinition { Chapter = 99, Name = name, Kind = kind, Argument = arg, State = state, AlwaysRun = true });
        }

        private static string Numbers(double first, double second, double third) => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", first, second, third);
    }
}
