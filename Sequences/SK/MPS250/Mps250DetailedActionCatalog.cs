using System.Collections.Generic;
using System.Globalization;

namespace TestPlatform.TestSequences
{
    public static partial class Mps250ActionCatalog
    {
        public static IReadOnlyList<Mps250ActionDefinition> Create()
        {
            var x = new List<Mps250ActionDefinition>();
            AddChapter06(x); AddChapter07(x); AddChapter08(x); AddChapter09(x);
            AddChapter10(x); AddChapter11(x); AddChapter12(x); AddChapter13(x);
            AddChapter14(x); AddChapter15(x); AddChapter16(x); AddChapter17(x);
            AddCleanup(x);
            Require(x, "H06", "H02");
            Require(x, "J04", "J01");
            Require(x, "J05", "J02");
            Require(x, "J06", "J03");
            AssignStableIds(x);
            return x;
        }

        private static void AddChapter06(List<Mps250ActionDefinition> x)
        {
            A(x, 6, "6.1 检查34901A/34903A/34907A、VBUS及VBATT电源在线", Mps250ActionKind.CheckInstruments);
            A(x, 6, "6.2 关闭VBUS/VBATT及其它仪器输出", Mps250ActionKind.AllOutputsOff);
            Relay(x, 6, "6.3 断开DAQ301-306功能继电器", Mps250StationMap.AllControlledRelays, false);
        }

        private static void AddChapter07(List<Mps250ActionDefinition> x)
        {
            Vbus(x, 7, "7.1 设置VBUS 275V/2A OVP300", Sk250TestSetpoints.Mps250.VbusNominal, false);
            Relay(x, 7, "7.2 闭合Relay_3(DAQ301) withdrawal接点", "3", true);
            VbusStable(x, 7, "7.1 功率路径闭合后打开VBUS输出并等待实测电压稳定", Sk250TestSetpoints.Mps250.VbusNominal);
            Daq(x, 7, "B01", "7.3.I 测量+5V DAQ101", Mps250StationMap.FiveVolt);
            Daq(x, 7, "B02", "7.3.II 测量+VCC DAQ102", Mps250StationMap.Vcc);
            Daq(x, 7, "B03", "7.3.III 测量-VEE DAQ103", Mps250StationMap.Vee);
            Daq(x, 7, "B04", "7.3.IV 测量+5VI DAQ119", Mps250StationMap.FiveVoltIsolated);
            Daq(x, 7, "B05", "7.3.V 测量VPWM DAQ114", Mps250StationMap.Vpwm);
            Shunt(x, 7, "B06", "7.3.VI 通过1Ω分流测量VBUS吸收电流 DAQ117", Mps250StationMap.VbusShunt, Mps250StationMap.VbusShuntOhms);
            Daq(x, 7, "B07", "7.3.VII 测量P+ DAQ113", Mps250StationMap.PPlus);
            Vbus(x, 7, "7.4 关闭VBUS输出", Sk250TestSetpoints.Mps250.VbusNominal, false);
            A(x, 7, "7.5 关闭本章仪器串口会话", Mps250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter08(List<Mps250ActionDefinition> x)
        {
            Vbus(x, 8, "8.1 初始化VBUS模拟器0V/0A OVP300", Sk250TestSetpoints.Common.OutputOffOvp300, false);
            Wait(x, 8, "8.2 等待1000ms");
            Vbus(x, 8, "8.3 设置VBUS起始点5V/0.5A OVP300", Sk250TestSetpoints.Mps250.VbusIgnitionStart, false);
            Vbus(x, 8, "8.4 打开VBUS输出", Sk250TestSetpoints.Mps250.VbusIgnitionStart, true);
            Wait(x, 8, "8.5 等待2000ms");
            Result(x, 8, "C01", "8.6-8.8 逐步升高VBUS并记录+5V超过4.8V时的点火电压", Mps250ActionKind.IgnitionRamp, IgnitionRampArgument(), Mps250StationMap.FiveVolt);
            Vbus(x, 8, "8.9 关闭VBUS输出", Sk250TestSetpoints.Common.OutputOffOvp300, false);
            A(x, 8, "8.10 关闭串口会话", Mps250ActionKind.CloseMbdCommunication);
        }

        private static void AddChapter09(List<Mps250ActionDefinition> x)
        {
            Vbus(x, 9, "9.1 设置VBUS 275V/2A OVP300", Sk250TestSetpoints.Mps250.VbusNominal, false);
            VbusStable(x, 9, "9.1 打开VBUS输出并等待实测电压稳定", Sk250TestSetpoints.Mps250.VbusNominal);
            Daq(x, 9, "D01", "9.2.I 测量COM_USB DAQ111", Mps250StationMap.ComUsb);
            Daq(x, 9, "D02", "9.2.II 测量3V3 DAQ120", Mps250StationMap.ThreeVoltThree);
            Wait(x, 9, "9.3 等待3000ms");
            A(x, 9, "9.4 初始化USB-B目标板串口115200 8N1", Mps250ActionKind.CheckMbdCommunication);
            Cmd(x, 9, "9.5 发送EXIT进入MCU终端", "EXIT");
            Result(x, 9, "D03", "9.6-9.7 发送R0并保存固件版本", Mps250ActionKind.FirmwareVersion, "R0");
            Vbus(x, 9, "9.8 关闭VBUS输出", Sk250TestSetpoints.Mps250.VbusNominal, false);
            A(x, 9, "9.9 关闭目标板串口通信", Mps250ActionKind.CloseMbdCommunication);
        }

        private static void AddChapter10(List<Mps250ActionDefinition> x)
        {
            Relay(x, 10, "10.1 断开Relay_2/2BIS(DAQ305/306)使+5VEXT与GND_EXT浮空", "2", false);
            Vbus(x, 10, "10.2 设置VBUS 275V/2A OVP300", Sk250TestSetpoints.Mps250.VbusNominal, false);
            Vbus(x, 10, "10.2 打开VBUS输出", Sk250TestSetpoints.Mps250.VbusNominal, true);
            A(x, 10, "10.3 文档标记为Step removed", Mps250ActionKind.DocumentedBoundary);
            Cmd(x, 10, "10.4 发送EXIT", "EXIT");
            Mbd(x, 10, "E01", "10.5 读取R78 st_remoto（外部电源断开）", "R78");
            Relay(x, 10, "10.6 闭合Relay_2/2BIS(DAQ305/306)连接外部5V和GND", "2", true);
            Wait(x, 10, "10.7 等待200ms");
            Mbd(x, 10, "E02", "10.8 读取R78 st_remoto（外部电源接通）", "R78");
            Relay(x, 10, "10.9 再次断开Relay_2/2BIS(DAQ305/306)", "2", false);
        }

        private static void AddChapter11(List<Mps250ActionDefinition> x)
        {
            Cmd(x, 11, "11.1 发送EXIT", "EXIT");
            Cmd(x, 11, "11.2 发送DIAG ON", "DIAG ON");
            Cmd(x, 11, "11.3 设置主开关PWM W101=700", "W101=700");
            Wait(x, 11, "11.4 等待500ms");
            Mbd(x, 11, "F01", "11.5 读取R79 withdrawal_status", "R79");
            Daq(x, 11, "F02", "11.6 测量OPEN_PREC_RL DAQ106", Mps250StationMap.OpenPrec);
            DaqAvg(x, 11, "F03", "11.7 U24.7 DAQ109采样16次/100ms取平均", Mps250StationMap.U24Pin7, 16, 100);
            Relay(x, 11, "11.8 断开Relay_3(DAQ301) withdrawal接点", "3", false);
            Wait(x, 11, "11.9 等待500ms");
            Mbd(x, 11, "F04", "11.10 读取R79 withdrawal_state", "R79");
            Daq(x, 11, "F05", "11.11 测量OPEN_PREC_RL DAQ106", Mps250StationMap.OpenPrec);
            DaqAvg(x, 11, "F06", "11.12 U24.7 DAQ109采样16次/100ms取平均", Mps250StationMap.U24Pin7, 16, 100);
            Cmd(x, 11, "11.13 关闭主开关 W101=0", "W101=0");
            Relay(x, 11, "11.14 闭合Relay_4(DAQ302)强制CONTACT_80V=0", "4", true);
            Wait(x, 11, "11.15 等待500ms");
            Mbd(x, 11, "F07", "11.16 读取R357 80V_PRESENCE低态", "R357");
            Relay(x, 11, "11.17 断开Relay_4(DAQ302)强制CONTACT_80V=1", "4", false);
            Wait(x, 11, "11.18 等待500ms");
            Mbd(x, 11, "F08", "11.19 读取R357VERB 80V_PRESENCE高态（PDF F8）", "R357VERB");
            Relay(x, 11, "11.20 恢复闭合Relay_3(DAQ301) withdrawal接点", "3", true);
        }

        private static void AddChapter12(List<Mps250ActionDefinition> x)
        {
            Cmd(x, 12, "12.1 发送EXIT", "EXIT"); Cmd(x, 12, "12.2 发送DIAG ON", "DIAG ON");
            Cmd(x, 12, "12.3 闭合报警继电器 W89=1", "W89=1"); Wait(x, 12, "12.4 等待500ms");
            Resistance(x, 12, "G01", "12.5 测量LS1 COM-NO闭合电阻 DAQ118", Mps250StationMap.AlarmContact);
            Cmd(x, 12, "12.6 断开报警继电器 W89=0", "W89=0"); Wait(x, 12, "12.7 等待500ms");
            Resistance(x, 12, "G02", "12.8 测量LS1 COM-NO断开电阻 DAQ118", Mps250StationMap.AlarmContact);
        }

        private static void AddChapter13(List<Mps250ActionDefinition> x)
        {
            Cmd(x, 13, "13.1 发送EXIT", "EXIT"); Cmd(x, 13, "13.2 发送DIAG ON", "DIAG ON");
            DaqAvg(x, 13, "H01", "13.3 U24.7静态采样16次/100ms", Mps250StationMap.U24Pin7, 16, 100);
            Shunt(x, 13, "H02", "13.4 通过1Ω分流测量MPS静态电流", Mps250StationMap.VbusShunt, Mps250StationMap.VbusShuntOhms);
            Cmd(x, 13, "13.5 设置pmos_sw_mcu W96=0", "W96=0");
            Cmd(x, 13, "13.6 设置主开关PWM W101=700", "W101=700"); Wait(x, 13, "13.7 等待200ms");
            DaqAvg(x, 13, "H03", "13.8 U24.7高驱动采样16次/100ms", Mps250StationMap.U24Pin7, 16, 100);
            Shunt(x, 13, "H04", "13.9 测量高驱动MPS电流", Mps250StationMap.VbusShunt, Mps250StationMap.VbusShuntOhms);
            Cmd(x, 13, "13.10 设置主开关PWM W101=200", "W101=200");
            DaqAvg(x, 13, "H05", "13.11 U24.7低驱动采样16次/100ms", Mps250StationMap.U24Pin7, 16, 100);
            Shunt(x, 13, "H06", "13.12 测量低驱动电流并与H02比较", Mps250StationMap.VbusShunt, Mps250StationMap.VbusShuntOhms);
            Wait(x, 13, "13.13 等待200ms"); Cmd(x, 13, "13.14 关闭主开关 W101=0", "W101=0");
            Cmd(x, 13, "13.15 关闭pmos_sw_mcu W96=5", "W96=5"); Wait(x, 13, "13.16 等待500ms");
            DaqAvg(x, 13, "H07", "13.17 U24.7关闭后采样16次/100ms（PDF H7）", Mps250StationMap.U24Pin7, 16, 100);
            Shunt(x, 13, "H08", "13.18 测量关闭后MPS电流（PDF H8）", Mps250StationMap.VbusShunt, Mps250StationMap.VbusShuntOhms);
            Cmd(x, 13, "13.19 安全确认主开关 W101=0", "W101=0");
        }

        private static void AddChapter14(List<Mps250ActionDefinition> x)
        {
            Cmd(x, 14, "14.1 发送EXIT", "EXIT"); Cmd(x, 14, "14.2 发送DIAG ON", "DIAG ON");
            Cmd(x, 14, "14.3 设置pmos_sw_mcu W96=0", "W96=0");
            Cmd(x, 14, "14.4 设置主开关 W101=700", "W101=700");
            Cmd(x, 14, "14.5 设置主开关 W101=200", "W101=200");
            A(x, 14, "14.6 文档标记为Removed", Mps250ActionKind.DocumentedBoundary); Wait(x, 14, "14.7 等待600ms");
            DaqAvg(x, 14, "I01", "14.8 U24.7按配置次数采样/100ms", Mps250StationMap.U24Pin7, 16, 100);
            Mbd(x, 14, "I02", "14.9 读取R81 st_westngh", "R81");
            Cmd(x, 14, "14.10 置位west_cmd W95=1", "W95=1"); Wait(x, 14, "14.11 等待600ms");
            DaqAvg(x, 14, "I03", "14.12 U24.7急停后按配置次数采样/100ms", Mps250StationMap.U24Pin7, 16, 100);
            Mbd(x, 14, "I04", "14.13 读取R81 mcu_west_stat", "R81");
            Cmd(x, 14, "14.14 关闭主开关 W101=0", "W101=0"); Cmd(x, 14, "14.15 清除west_cmd W95=0", "W95=0");
            Wait(x, 14, "14.16 等待200ms"); Cmd(x, 14, "14.17 关闭pmos_sw_mcu W96=5", "W96=5");
        }

        private static void AddChapter15(List<Mps250ActionDefinition> x)
        {
            Cmd(x, 15, "15.1 发送EXIT", "EXIT"); Cmd(x, 15, "15.2 发送DIAG ON", "DIAG ON");
            Mbd(x, 15, "J01", "15.3 读取R69VERB adc3 vbus+ PA3", "R69VERB");
            SafeVbusRelay(x, 15, "15.4 关闭VBUS、闭合Relay_5(DAQ303)后恢复275V输出", "5", Sk250TestSetpoints.Mps250.VbusNominal, true); Wait(x, 15, "15.5 等待500ms");
            Mbd(x, 15, "J02", "15.6 读取R68 VSensPrec PC1", "R68");
            Vbatt(x, 15, "15.7 设置VBATT 275V/0.1A OVP300", Sk250TestSetpoints.Mps250.VbattCalibration, false);
            Vbatt(x, 15, "15.7 打开VBATT输出", Sk250TestSetpoints.Mps250.VbattCalibration, true); Wait(x, 15, "15.8 等待5000ms");
            Cmd(x, 15, "15.9 闭合内部RL1 W91=1", "W91=1"); Wait(x, 15, "15.10 等待500ms");
            Mbd(x, 15, "J03", "15.11 读取R70 adc2 vstring2+ PA2", "R70");
            A(x, 15, "15.12 DAQ113独立测量P+ VBUS参考电压", Mps250ActionKind.DaqVoltage, Mps250StationMap.VbusReference.ToString(CultureInfo.InvariantCulture));
            Result(x, 15, "J04", "15.13 计算Gain_VBUS=4096*VBUS/J01", Mps250ActionKind.ComputedGain, "J01", Mps250StationMap.VbusReference);
            Result(x, 15, "J05", "15.14 计算Gain_VPREC=4096*VBUS/J02", Mps250ActionKind.ComputedGain, "J02", Mps250StationMap.VbusReference);
            Result(x, 15, "J06", "15.15-16 计算单路Gain_VSTRING2=4096*VBUS/J03", Mps250ActionKind.ComputedGain, "J03", Mps250StationMap.VbusReference);
            Cmd(x, 15, "15.17 断开内部RL1 W91=0", "W91=0"); SafeVbusRelay(x, 15, "15.18 关闭VBUS、断开Relay_5(DAQ303)后恢复275V输出", "5", Sk250TestSetpoints.Mps250.VbusNominal, false);
            Vbatt(x, 15, "15.19 关闭VBATT输出", Sk250TestSetpoints.Common.OutputOffOvp300, false);
        }

        private static void AddChapter16(List<Mps250ActionDefinition> x)
        {
            Cmd(x, 16, "16.1 发送EXIT", "EXIT"); Cmd(x, 16, "16.2 发送DIAG ON", "DIAG ON");
            Result(x, 16, "K01", "16.3 断开Relay_6并等待VBATT≤0.5V后内部定时采样8秒/100ms DAQ105",
                Mps250ActionKind.DaqTimedWindowAverageVoltageAfterVbattBaseline, "6", Mps250StationMap.McuIsoBatt, 0, 81, 100);
            Vbatt(x, 16, "16.4 预设VBATT 0V/0.1A OVP300并保持输出关闭", Sk250TestSetpoints.Mps250.VbattZero, false);
            A(x, 16, "16.5 在VBATT关闭时闭合Relay_6(DAQ304)，输出0V并验证实测≤0.5V", Mps250ActionKind.SafeVbattConnect, "6");
            DaqWindow(x, 16, "K02", "16.6 MCU_ISOBATT@0V内部定时采样8秒/100ms DAQ105", Mps250StationMap.McuIsoBatt);
            A(x, 16, "16.7 设置VBATT 250V/0.1A OVP300并等待实测稳定", Mps250ActionKind.SetVbattAndWaitStable,
                Sk250TestSetpoints.Mps250.VbattIsolation.ToArgument(), true);
            DaqWindow(x, 16, "K03", "16.8 MCU_ISOBATT@250V内部定时采样8秒/100ms DAQ105", Mps250StationMap.McuIsoBatt);
            A(x, 16, "16.9 先关闭VBATT再断开Relay_6(DAQ304) ISOBATT_INPUT", Mps250ActionKind.SafeVbattDisconnect, "6", false); Vbatt(x, 16, "16.10 确认VBATT保持关闭", Sk250TestSetpoints.Common.OutputOffOvp300, false);
        }

        private static void AddChapter17(List<Mps250ActionDefinition> x)
        {
            Cmd(x, 17, "17.1 发送EXIT", "EXIT"); Cmd(x, 17, "17.2 发送DIAG ON", "DIAG ON");
            Cmd(x, 17, "17.3 进入SAFE终端 REMOTE 31", "REMOTE 31"); Cmd(x, 17, "17.4 SAFE关闭risc_on W38=0", "W38=0");
            Cmd(x, 17, "17.5 返回MCU终端 EXIT", "EXIT"); Cmd(x, 17, "17.6 发送DIAG ON", "DIAG ON");
            Cmd(x, 17, "17.7 关闭PWM1 W99=0", "W99=0"); Cmd(x, 17, "17.8 关闭PWM2 W100=0", "W100=0");
            Cmd(x, 17, "17.9 打开MCU risc_on W92=1", "W92=1"); Wait(x, 17, "17.10 等待200ms");
            Mbd(x, 17, "L01", "17.11 读取R71 ridstrneg基线", "R71");
            DaqAvg(x, 17, "L02", "17.12-13 FBK_1 DAQ107采样16次/100ms", Mps250StationMap.HeaterFeedback1, 16, 100);
            DaqAvg(x, 17, "L03", "17.12-14 FBK_2 DAQ108采样16次/100ms", Mps250StationMap.HeaterFeedback2, 16, 100);
            Shunt(x, 17, "L04", "17.15 测量加热器关闭时MPS电流", Mps250StationMap.VbusShunt, Mps250StationMap.VbusShuntOhms);
            Cmd(x, 17, "17.16 激活PWM1 W99=1000", "W99=1000"); Wait(x, 17, "17.17 等待10000ms");
            Mbd(x, 17, "L05", "17.18 读取R71 ridstrneg（PWM1开启）", "R71");
            DaqAvg(x, 17, "L06", "17.19-20 FBK_1 100%状态采样16次/100ms", Mps250StationMap.HeaterFeedback1, 16, 100);
            Shunt(x, 17, "L07", "17.21 测量PWM1开启时MPS电流", Mps250StationMap.VbusShunt, Mps250StationMap.VbusShuntOhms);
            Cmd(x, 17, "17.22 关闭PWM1 W99=0", "W99=0"); Wait(x, 17, "17.22 等待10000ms");
            DaqAvg(x, 17, "L08", "17.23 FBK_1恢复0%采样16次/100ms", Mps250StationMap.HeaterFeedback1, 16, 100);
            Wait(x, 17, "17.24 等待500ms"); Cmd(x, 17, "17.25 激活PWM2 W100=1000", "W100=1000"); Wait(x, 17, "17.26 等待10000ms");
            Mbd(x, 17, "L09", "17.27 读取R71 ridstrneg（PWM2开启）", "R71");
            DaqAvg(x, 17, "L10", "17.28-29 FBK_2 100%状态采样16次/100ms", Mps250StationMap.HeaterFeedback2, 16, 100);
            Shunt(x, 17, "L11", "17.30 测量PWM2开启时MPS电流", Mps250StationMap.VbusShunt, Mps250StationMap.VbusShuntOhms);
            Cmd(x, 17, "17.31 关闭两路加热器risc_on W92=0", "W92=0"); Wait(x, 17, "17.31 等待10000ms");
            DaqAvg(x, 17, "L12", "17.32 FBK_2恢复0%采样16次/100ms", Mps250StationMap.HeaterFeedback2, 16, 100);
            Wait(x, 17, "17.33 等待1000ms"); Mbd(x, 17, "L13", "17.34 读取R71 ridstrneg（risc_on关闭）", "R71");
            Cmd(x, 17, "17.35 确认PWM1关闭 W99=0", "W99=0"); Cmd(x, 17, "17.36 确认PWM2关闭 W100=0", "W100=0");
            Wait(x, 17, "17.36 等待10000ms");
            DaqAvg(x, 17, "L14", "17.36-37 FBK_1最终采样16次/100ms", Mps250StationMap.HeaterFeedback1, 16, 100);
            DaqAvg(x, 17, "L15", "17.36-38 FBK_2最终采样16次/100ms", Mps250StationMap.HeaterFeedback2, 16, 100);
            Shunt(x, 17, "L16", "17.39 测量最终MPS电流", Mps250StationMap.VbusShunt, Mps250StationMap.VbusShuntOhms);
            Vbus(x, 17, "17.40 关闭VBUS输出", Sk250TestSetpoints.Common.OutputOffOvp300, false);
            Relay(x, 17, "17.42 断开Relay_3(DAQ301) withdrawal接点", "3", false); A(x, 17, "17.43 关闭目标板串口通信", Mps250ActionKind.CloseMbdCommunication);
        }

        private static void AddCleanup(List<Mps250ActionDefinition> x)
        {
            Cleanup(x, "若USB仍连接则发送主开关/加热器安全关闭命令", Mps250ActionKind.MbdCleanupIfConnected, "W101=0;W95=0;W96=5;W99=0;W100=0;W92=0", true);
            Cleanup(x, "关闭VBUS输出", Mps250ActionKind.SetVbus, Sk250TestSetpoints.Common.OutputOffOvp300.ToArgument(), false);
            Cleanup(x, "关闭VBATT输出", Mps250ActionKind.SetVbatt, Sk250TestSetpoints.Common.OutputOffOvp300.ToArgument(), false);
            Cleanup(x, "断开DAQ301-306功能继电器", Mps250ActionKind.DaqRelays, Mps250StationMap.AllControlledRelays, false);
            Cleanup(x, "关闭所有仪器输出", Mps250ActionKind.AllOutputsOff, null, false);
            Cleanup(x, "关闭目标板通信并释放治具", Mps250ActionKind.Cleanup, null, false);
        }

        private static void A(List<Mps250ActionDefinition> x, int ch, string name, Mps250ActionKind kind, string arg = null, bool state = true) => x.Add(new Mps250ActionDefinition { Chapter = ch, Name = name, Kind = kind, Argument = arg, State = state });
        private static void Result(List<Mps250ActionDefinition> x, int ch, string id, string name, Mps250ActionKind kind, string arg = null, int channel = 0, double scale = 0, int samples = 1, int interval = 0) => x.Add(new Mps250ActionDefinition { Chapter = ch, Name = id + " " + name, Kind = kind, Argument = arg, ResultId = id, Channel = channel, Scale = scale, SampleCount = samples, SampleIntervalMs = interval, State = true });
        private static void Relay(List<Mps250ActionDefinition> x, int ch, string name, string relays, bool state) => A(x, ch, name, Mps250ActionKind.DaqRelays, relays, state);
        private static void Cmd(List<Mps250ActionDefinition> x, int ch, string name, string command) => A(x, ch, name, Mps250ActionKind.MbdCommand, command);
        private static void Wait(List<Mps250ActionDefinition> x, int ch, string name) => A(x, ch, name, Mps250ActionKind.Delay);
        private static void Vbus(List<Mps250ActionDefinition> x, int ch, string name, Sk250SourceSetpoint setpoint, bool state) => A(x, ch, name, Mps250ActionKind.SetVbus, setpoint.ToArgument(), state);
        private static void VbusStable(List<Mps250ActionDefinition> x, int ch, string name, Sk250SourceSetpoint setpoint) => A(x, ch, name, Mps250ActionKind.SetVbusAndWaitStable, setpoint.ToArgument(), true);
        private static void Vbatt(List<Mps250ActionDefinition> x, int ch, string name, Sk250SourceSetpoint setpoint, bool state) => A(x, ch, name, Mps250ActionKind.SetVbatt, setpoint.ToArgument(), state);
        private static void Vbus(List<Mps250ActionDefinition> x, int ch, string name, double v, double a, double ovp, bool state) => A(x, ch, name, Mps250ActionKind.SetVbus, Numbers(v, a, ovp), state);
        private static void Vbatt(List<Mps250ActionDefinition> x, int ch, string name, double v, double a, double ovp, bool state) => A(x, ch, name, Mps250ActionKind.SetVbatt, Numbers(v, a, ovp), state);
        private static void SafeVbusRelay(List<Mps250ActionDefinition> x, int ch, string name, string relay, Sk250SourceSetpoint setpoint, bool state) => A(x, ch, name, Mps250ActionKind.SafeVbusRelay, relay + "," + setpoint.ToArgument() + ",1", state);
        private static void Mbd(List<Mps250ActionDefinition> x, int ch, string id, string name, string command) => Result(x, ch, id, name, Mps250ActionKind.MbdNumeric, command);
        private static void Daq(List<Mps250ActionDefinition> x, int ch, string id, string name, int channel) => Result(x, ch, id, name, Mps250ActionKind.DaqVoltage, null, channel);
        private static void DaqAvg(List<Mps250ActionDefinition> x, int ch, string id, string name, int channel, int samples, int interval) => Result(x, ch, id, name, Mps250ActionKind.DaqAverageVoltage, null, channel, 0, samples, interval);
        private static void DaqWindow(List<Mps250ActionDefinition> x, int ch, string id, string name, int channel) => Result(x, ch, id, name, Mps250ActionKind.DaqTimedWindowAverageVoltage, null, channel, 0, 81, 100);
        private static void Shunt(List<Mps250ActionDefinition> x, int ch, string id, string name, int channel, double ohms) => Result(x, ch, id, name, Mps250ActionKind.DaqShuntCurrent, null, channel, ohms);
        private static void Resistance(List<Mps250ActionDefinition> x, int ch, string id, string name, int channel) => Result(x, ch, id, name, Mps250ActionKind.DaqResistance, null, channel);
        private static void Cleanup(List<Mps250ActionDefinition> x, string name, Mps250ActionKind kind, string arg, bool state) => x.Add(new Mps250ActionDefinition { Chapter = 99, Name = name, Kind = kind, Argument = arg, State = state, AlwaysRun = true });
        private static string Numbers(double a, double b, double c) => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", a, b, c);
        private static string IgnitionRampArgument() => Numbers(
            Sk250TestSetpoints.Mps250.IgnitionRampStartVoltage,
            Sk250TestSetpoints.Mps250.IgnitionRampEndVoltage,
            Sk250TestSetpoints.Mps250.IgnitionRampStepVoltage);
    }
}
