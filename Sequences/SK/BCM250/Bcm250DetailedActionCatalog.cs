using System;
using System.Collections.Generic;

namespace TestPlatform.TestSequences
{
    /// <summary>
    /// BCM-250 Rev.5 procedure catalogue.
    ///
    /// Unlike the legacy catalogue, this list does not collapse a complete
    /// procedure paragraph into one UI row. Commands, waits, relay changes,
    /// instrument set-points, measurements, programming gates, and shutdown
    /// actions are represented independently, matching the BCM-125 operator
    /// feedback style.
    /// </summary>
    public static partial class Bcm250ActionCatalog
    {
        public static IReadOnlyList<Bcm250ActionDefinition> Create()
        {
            var items = new List<Bcm250ActionDefinition>();

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
            AddChapter18(items);
            AddChapter19(items);
            AddChapter20(items);
            AddChapter21(items);
            AddChapter22(items);
            AddChapter23(items);
            AddChapter24(items);
            AddChapter25(items);
            AddChapter26(items);
            AddChapter27(items);
            AddDetailedCleanup(items);

            Require(items, "J02", "J01");
            Require(items, "M05", "M01", "M02", "M03", "M04");
            Require(items, "M06", "M05");
            Require(items, "M07", "M05");
            Require(items, "M08", "M06");
            Require(items, "M09", "M07", "M08");
            Require(items, "N05", "N01", "N02", "N03", "N04");
            Require(items, "N06", "N01", "N02", "N03", "N04");
            Require(items, "N07", "N05", "N06");
            Require(items, "N08", "N05", "N06");
            Require(items, "Q12", "Q01", "Q03", "Q05", "Q07", "Q08", "Q10", "Q11");
            Require(items, "Q13", "Q12");
            Require(items, "Q14", "Q02", "Q06", "Q09", "Q10");
            Require(items, "Q15", "Q12", "Q14");
            Require(items, "Q16", "Q14", "Q15");

            AssignStableIds(items);
            return items;
        }

        private static void AddChapter07(List<Bcm250ActionDefinition> items)
        {
            Action(items, 7, "1 检查34901A/34903A/34907A、VBUS、String及电子负载在线",
                Bcm250ActionKind.CheckInstruments);
            Action(items, 7, "2 关闭并复位全部仪器输出", Bcm250ActionKind.AllOutputsOff);
            Fixture(items, 7, "3 断开全部BCM-250测试继电器",
                Bcm250StationMap.AllBcm250Relays, false);
            ShuntResistance(items, 7, "4 断电测量DAQ113外部String分流电阻并保存",
                Bcm250StationMap.StringShuntDaqChannel, Bcm250StationMap.StringShuntOhms);
            ShuntResistance(items, 7, "5 断电测量DAQ117 +5V分流电阻并保存",
                Bcm250StationMap.FiveVoltShuntDaqChannel, Bcm250StationMap.FiveVoltShuntOhms);
            ShuntResistance(items, 7, "6 断电测量DAQ119 +VCC分流电阻并保存",
                Bcm250StationMap.VccShuntDaqChannel, Bcm250StationMap.VccShuntOhms);
        }

        private static void AddChapter08(List<Bcm250ActionDefinition> items)
        {
            MpsPower(items, 8, "1 配置ANS/MPS板电源275V/1A并保持输出关闭",
                Sk250TestSetpoints.Bcm250.MpsBoardVoltage,
                Sk250TestSetpoints.Bcm250.MpsBoardCurrent,
                false);
            Action(items, 8, "2 闭合RL4后开启ANS/MPS板电源并等待2000ms稳定", Bcm250ActionKind.SafeAnsPowerOn, "7,275,1", true);
            Daq(items, 8, "B01", "3.I 测量+5V DAQ CH01", Bcm250StationMap.FiveVoltDaqChannel);
            Daq(items, 8, "B02", "3.II 测量+VCC DAQ CH02", Bcm250StationMap.VccDaqChannel);
            Daq(items, 8, "B03", "3.III 测量-VEE DAQ CH03", Bcm250StationMap.VeeDaqChannel);
            Daq(items, 8, "B04", "3.IV 测量3V3A DAQ CH07", Bcm250StationMap.ThreeVoltThreeAnalogDaqChannel);
            Daq(items, 8, "B05", "3.V 测量3V3-1 DAQ CH11", Bcm250StationMap.ThreeVoltThreeDaqChannel);
            Shunt(items, 8, "B06", "3.VI 通过2.0R分流测量+VCC电流",
                Bcm250StationMap.VccShuntDaqChannel, Bcm250StationMap.VccShuntOhms);
            Shunt(items, 8, "B07", "3.VII 通过0.4R分流测量+5V电流",
                Bcm250StationMap.FiveVoltShuntDaqChannel, Bcm250StationMap.FiveVoltShuntOhms);
        }

        private static void AddChapter09(List<Bcm250ActionDefinition> items)
        {
            Action(items, 9, "1 检查String/VBUS/电子负载及TTL编程接口",
                Bcm250ActionKind.CheckInstruments);
            Vbus(items, 9, "1.b 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            String(items, 9, "1.c 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Load(items, 9, "1.d 复位电子负载CV 0V/0A/0W", 0, 0, 0, false);
            Fixture(items, 9, "2 闭合TTL TX/RX/GND编程继电器",
                Bcm250StationMap.ProgrammingFixtureRelays, true);
            Wait(items, 9, "3 等待300ms");
            Power(items, 9, "4 闭合RL4给DUT供电", true);
            Wait(items, 9, "5 等待6000ms");
            Action(items, 9, "6 DUT上电后发送0D识别并打开USB-B/MBD串口",
                Bcm250ActionKind.CheckMbdCommunication);
            Wait(items, 9, "7 等待50ms");
            Command(items, 9, "8 发送EXIT进入MBD终端", "EXIT");
            Wait(items, 9, "9 等待50ms");
            Command(items, 9, "10 发送DIAG ON到MBD", "DIAG ON");
            Wait(items, 9, "11 等待50ms");
            Command(items, 9, "12 RESET置位W86=1", "W86=1");
            Wait(items, 9, "13 等待50ms");
            DaqRelay(items, 9, "14 闭合BOOT0 DAQ305继电器",
                Bcm250StationMap.ProgrammingBootDaqRelay, true);
            Wait(items, 9, "15 等待50ms");
            Command(items, 9, "16 RESET释放W86=0", "W86=0");
            Result(items, 9, "C01", "17 TTL烧录BCM_250_V5.HEX（实机工具/端口门禁）",
                Bcm250ActionKind.ProgramFirmware, Bcm250Commands.FirmwareFile);
            Command(items, 9, "19 烧录后RESET置位W86=1", "W86=1");
            DaqRelay(items, 9, "20 断开BOOT0 DAQ305继电器",
                Bcm250StationMap.ProgrammingBootDaqRelay, false);
            Command(items, 9, "21 RESET释放W86=0", "W86=0");
            Fixture(items, 9, "22 断开TTL编程继电器",
                Bcm250StationMap.ProgrammingFixtureRelays, false);
        }

        private static void AddChapter10(List<Bcm250ActionDefinition> items)
        {
            Action(items, 10, "1 先关闭ANS输出再断开RL4", Bcm250ActionKind.SafeAnsPowerOff, Bcm250StationMap.DutPowerRelay.ToString(), false);
            Wait(items, 10, "2 等待6500ms");
            Action(items, 10, "3 闭合RL4后重新开启ANS给DUT供电", Bcm250ActionKind.SafeAnsPowerOn, "7,275,1", true);
            Action(items, 10, "4 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 10, "5.I 发送EXIT进入MBD终端", "EXIT");
            Mbd(items, 10, "D01", "5.II 读取R14 BCM内部CAN状态", "R14");
            Mbd(items, 10, "D02", "5.II 读取R15VERB确认SAFE内部CAN状态", "R15VERB");
            Action(items, 10, "5.IV 结束本章串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter11(List<Bcm250ActionDefinition> items)
        {
            Power(items, 11, "1 确认RL4闭合给DUT供电", true);
            Action(items, 11, "2 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 11, "3 发送REMOTE2进入BCM250终端", "REMOTE2");
            Result(items, 11, "E01", "4 读取R0并核对BCM固件版本",
                Bcm250ActionKind.FirmwareVersion, "R0");
            Command(items, 11, "5 发送EXIT返回MBD终端", "EXIT");
            Action(items, 11, "7 结束本章串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter12(List<Bcm250ActionDefinition> items)
        {
            Power(items, 12, "1 确认RL4闭合给DUT供电", true);
            Wait(items, 12, "2 等待3000ms");
            Action(items, 12, "3 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 12, "4 发送EXIT进入MBD终端", "EXIT");
            Command(items, 12, "5 发送DIAG ON到MBD", "DIAG ON");
            Command(items, 12, "6 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 12, "7 读取复位前R10 running_time", "R10");
            Command(items, 12, "8 发送EXIT返回MBD终端", "EXIT");
            Command(items, 12, "9 RESET置位W86=1", "W86=1");
            Command(items, 12, "10 RESET释放W86=0", "W86=0");
            Wait(items, 12, "11 等待1000ms");
            Command(items, 12, "12 重新发送REMOTE2进入BCM250终端", "REMOTE2");
            Result(items, 12, "F01", "13 读取复位后R10并判定平线复位",
                Bcm250ActionKind.LogicResult, "R10");
            Command(items, 12, "16 发送EXIT返回MBD终端", "EXIT");
            Command(items, 12, "17 确认RESET释放W86=0", "W86=0");
            Action(items, 12, "18 结束本章串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter13(List<Bcm250ActionDefinition> items)
        {
            Power(items, 13, "1 确认RL4闭合给DUT供电", true);
            Action(items, 13, "2 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 13, "3 发送EXIT进入MBD终端", "EXIT");
            Command(items, 13, "5 发送REMOTE2进入BCM250终端", "REMOTE2");
            Mbd(items, 13, "G01", "6 读取R55 adc13 vref PC3", "R55");
            Command(items, 13, "8 发送EXIT返回MBD终端", "EXIT");
            Action(items, 13, "10 结束本章串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter14(List<Bcm250ActionDefinition> items)
        {
            Power(items, 14, "1 确认RL4闭合给DUT供电", true);
            Action(items, 14, "2 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 14, "3 发送EXIT进入MBD终端", "EXIT");
            Command(items, 14, "4 发送REMOTE2进入BCM250终端", "REMOTE2");
            Mbd(items, 14, "H01", "5 读取R53 adc11 vcc PC1", "R53");
            Mbd(items, 14, "H02", "6 读取R54 adc12 vee PC2", "R54");
            Command(items, 14, "7 发送EXIT返回MBD终端", "EXIT");
            Action(items, 14, "10 结束本章串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter15(List<Bcm250ActionDefinition> items)
        {
            Power(items, 15, "1 确认RL4闭合给DUT供电", true);
            Action(items, 15, "2 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 15, "3 发送EXIT进入MBD终端", "EXIT");
            Command(items, 15, "4 发送REMOTE2进入BCM250终端", "REMOTE2");
            Mbd(items, 15, "I01", "5 读取R52 adc10 hs_temp PC0", "R52");
            Command(items, 15, "6 发送EXIT返回MBD终端", "EXIT");
            Action(items, 15, "8 结束本章串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter16(List<Bcm250ActionDefinition> items)
        {
            Power(items, 16, "1 确认RL4闭合给DUT供电", true);
            Action(items, 16, "2 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 16, "3 发送EXIT进入MBD终端", "EXIT");
            Command(items, 16, "4 发送DIAG ON到MBD", "DIAG ON");
            Command(items, 16, "5 激活MPS RL2：W94=1", "W94=1");
            Wait(items, 16, "6 等待5000ms");
            Command(items, 16, "7 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 16, "8 发送DIAG ON到BCM250", "DIAG ON");
            Command(items, 16, "9 读取R51VERB确认adc1 vstr_pos PA1", "R51VERB");
            Daq(items, 16, null, "10 DAQ105测量MPS J8.1/P+ VBUS参考", Bcm250StationMap.VbusReferenceDaqChannel);
            Result(items, 16, "J01", "11 计算Gain_VBUS并写入W18",
                Bcm250ActionKind.ComputedCalibration, "J01");
            Command(items, 16, "12 写入offset_vstr_pos W19=0", "W19=0");
            Command(items, 16, "12.b 保存VBUS校准参数ACT->FLASH", "ACT->FLASH");
            Mbd(items, 16, "J02", "13 回读R18核对Gain_VBUS", "R18");
            Mbd(items, 16, "J03", "14 回读R19核对offset_vstr_pos", "R19");
            Mbd(items, 16, "J04", "15 平均读取5次R43VERB vstr_pos", "R43VERB");
            Command(items, 16, "17 发送EXIT返回MBD终端", "EXIT");
            Command(items, 16, "18 发送DIAG ON到MBD", "DIAG ON");
            Command(items, 16, "19 关闭MPS RL2：W94=0", "W94=0");
            Action(items, 16, "20 结束本章串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter17(List<Bcm250ActionDefinition> items)
        {
            Power(items, 17, "1 确认RL4闭合给DUT供电", true);
            Action(items, 17, "2 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 17, "3 发送EXIT进入MBD终端", "EXIT");
            Command(items, 17, "4 发送DIAG ON到MBD", "DIAG ON");
            Command(items, 17, "4.a 设置drv_main_sw PE14：W101=1", "W101=1");
            items[items.Count - 1].StepId = "BCM250.CH17.S004A.ACTION";
            Command(items, 17, "5 MBD使能Westinghouse：W95=1", "W95=1");
            Command(items, 17, "6 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 17, "7 发送DIAG ON到BCM250", "DIAG ON");
            Mbd(items, 17, "K01", "8-9 读取R61确认st_emerg=0", "R61");
            Command(items, 17, "10 发送EXIT返回MBD终端", "EXIT");
            Command(items, 17, "10.a 发送DIAG ON到MBD", "DIAG ON");
            Command(items, 17, "11 MBD关闭Westinghouse：W95=0", "W95=0");
            Command(items, 17, "12 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 17, "12.a 发送DIAG ON到BCM250", "DIAG ON");
            Mbd(items, 17, "K02", "13 读取R61确认st_emerg=1", "R61");
            Command(items, 17, "14 DUT使能emerg_uc：W67=1", "W67=1");
            Mbd(items, 17, "K03", "15 读取R61确认DUT报警st_emerg=0", "R61");
            Command(items, 17, "16 发送EXIT返回MBD终端", "EXIT");
            Mbd(items, 17, "K04", "17-18 读取R81确认MBD st_westngh=0", "R81");
            Command(items, 17, "19 发送EXIT进入MBD终端", "EXIT");
            Command(items, 17, "20 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 17, "21 DUT关闭emerg_uc：W67=0", "W67=0");
            Mbd(items, 17, "K05", "22-23 读取R61确认st_emerg=1", "R61");
            Command(items, 17, "24 发送EXIT返回MBD终端", "EXIT");
            Mbd(items, 17, "K06", "25-26 读取R81确认MBD st_westngh=1", "R81");
            Command(items, 17, "28 发送EXIT返回MBD终端", "EXIT");
            Action(items, 17, "29 结束本章串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter18(List<Bcm250ActionDefinition> items)
        {
            Power(items, 18, "1 闭合RL4给DUT供电", true);
            Fixture(items, 18, "2 断开RL1a/RL1b/RL2功率继电器",
                Bcm250StationMap.PowerPathRelays, false);
            DaqRelay(items, 18, "3 断开RL5.12/U1.2 DAQ302", Bcm250StationMap.U12ConnectionDaqRelay, false);
            Wait(items, 18, "4 等待50ms");
            Vbus(items, 18, "5 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            Action(items, 18, "6 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 18, "7 发送DIAG ON到MBD", "DIAG ON");
            Command(items, 18, "8 读取R68确认C21放电", "R68");
            Command(items, 18, "9 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 18, "10 发送DIAG ON到BCM250", "DIAG ON");
            Command(items, 18, "11 预充电继电器关闭W75=0", "W75=0");
            Fixture(items, 18, "12 闭合RL1a_B/RL2B预充电路径", "9,8", true);
            Wait(items, 18, "13 等待500ms");
            Vbus(items, 18, "14 设置VBUS发生器275V/0.1A/OVP300", Sk250TestSetpoints.Bcm250.VbusCalibration, false);
            Vbus(items, 18, "15 打开VBUS发生器输出", Sk250TestSetpoints.Bcm250.VbusCalibration, true);
            Command(items, 18, "16 发送EXIT进入MBD终端", "EXIT");
            Command(items, 18, "17 发送DIAG ON到MBD", "DIAG ON");
            Wait(items, 18, "18 等待15000ms");
            Mbd(items, 18, "L01", "19 读取R69 adc3 vbus", "R69");
            Mbd(items, 18, "L02", "20 读取R68 VsensPrec", "R68");
            Command(items, 18, "21 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 18, "22 发送DIAG ON到BCM250", "DIAG ON");
            Command(items, 18, "23 激活预充电W75=1", "W75=1");
            Command(items, 18, "24 发送EXIT返回MBD终端", "EXIT");
            Command(items, 18, "25 发送DIAG ON到MBD", "DIAG ON");
            Wait(items, 18, "26 等待500ms");
            Mbd(items, 18, "L03", "27 读取R69预充电后adc3 vbus", "R69");
            Mbd(items, 18, "L04", "28 读取R68VERB确认预充电后VsensPrec", "R68VERB");
            Wait(items, 18, "29 等待4000ms");
            Vbus(items, 18, "29.b 关闭VBUS发生器", Sk250TestSetpoints.Bcm250.VbusCalibration, false);
            Vbus(items, 18, "30 设VBUS发生器0V/0A/OVP10", 0, 0, 10, false);
            Vbus(items, 18, "31 确认VBUS发生器输出关闭", 0, 0, 10, false);
            Vbus(items, 18, "32 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            Wait(items, 18, "33 等待15000ms");
            Command(items, 18, "34.a 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 18, "34.b 关闭预充电W75=0", "W75=0");
            Fixture(items, 18, "35 断开全部预充电功率继电器",
                Bcm250StationMap.PowerPathRelays, false);
            Wait(items, 18, "36 等待200ms");
            Action(items, 18, "38 结束仪器串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter19(List<Bcm250ActionDefinition> items)
        {
            Power(items, 19, "1 确认RL4闭合给DUT供电", true);
            Fixture(items, 19, "2 断开RL1a/RL1b/RL2功率继电器",
                Bcm250StationMap.PowerPathRelays, false);
            DaqRelay(items, 19, "3 断开RL5.12/U1.2 DAQ302", Bcm250StationMap.U12ConnectionDaqRelay, false);
            Wait(items, 19, "4 等待50ms");
            Vbus(items, 19, "5 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            Wait(items, 19, "6 等待20ms");
            DaqRelay(items, 19, "7 闭合VMID/RL6测量路径DAQ301", Bcm250StationMap.MidpointDaqRelay, true);
            Vbus(items, 19, "8 设置VMID高点125V/0.1A/OVP300并输出", Sk250TestSetpoints.Bcm250.VmidHigh, true);
            Wait(items, 19, "8.b 等待10000ms");
            Action(items, 19, "9 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 19, "10 发送EXIT进入MBD终端", "EXIT");
            Command(items, 19, "11 发送DIAG ON到MBD", "DIAG ON");
            Wait(items, 19, "12 等待500ms");
            Command(items, 19, "13 激活MPS volt_in2：W91=1", "W91=1");
            Wait(items, 19, "14 等待10000ms");
            Command(items, 19, "15 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 19, "16 发送DIAG ON到BCM250", "DIAG ON");
            Command(items, 19, "17 使能中点采样W76=1", "W76=1");
            Wait(items, 19, "18 等待2000ms");
            Mbd(items, 19, "M01", "19 平均读取8次R93VERB @125V", "R93VERB");
            Daq(items, 19, "M02", "20 DAQ106平均测量8次VMID @125V", Bcm250StationMap.VmidReferenceDaqChannel);
            Vbus(items, 19, "21 设置VMID低点5V/0.1A/OVP300并输出", Sk250TestSetpoints.Bcm250.VmidLow, true);
            Wait(items, 19, "22 等待12000ms");
            Mbd(items, 19, "M03", "23 平均读取8次R93 @5V", "R93");
            Daq(items, 19, "M04", "24 DAQ106平均测量8次VMID @5V", Bcm250StationMap.VmidReferenceDaqChannel);
            Result(items, 19, "M05", "25 计算Gain_VMID并写入W20",
                Bcm250ActionKind.ComputedCalibration, "M05");
            Result(items, 19, "M06", "26 计算offset_vstr_mid并写入W21",
                Bcm250ActionKind.ComputedCalibration, "M06");
            Command(items, 19, "27 保存VMID校准参数ACT->FLASH", "ACT->FLASH");
            Mbd(items, 19, "M07", "28 回读R20核对Gain_VMID", "R20");
            Mbd(items, 19, "M08", "29 回读R21核对offset_vstr_mid", "R21");
            Vbus(items, 19, "30 恢复VMID 125V/0.1A/OVP300并输出", Sk250TestSetpoints.Bcm250.VmidHigh, true);
            Wait(items, 19, "31 等待15000ms");
            Mbd(items, 19, "M09", "32-33 平均读取20次R92VERB vstr_mid1", "R92VERB");
            Command(items, 19, "34 发送EXIT返回MBD终端", "EXIT");
            Command(items, 19, "35 发送DIAG ON到MBD", "DIAG ON");
            Command(items, 19, "36 关闭MPS volt_in2：W91=0", "W91=0");
            Command(items, 19, "37 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 19, "38 发送DIAG ON到BCM250", "DIAG ON");
            Command(items, 19, "39 关闭中点采样W76=0", "W76=0");
            String(items, 19, "40 关闭String发生器输出", 0, 0, 10, false);
            String(items, 19, "41 设String发生器0V/0A/OVP10", 0, 0, 10, false);
            Vbus(items, 19, "42 关闭VBUS发生器输出", 0, 0, 300, false);
            String(items, 19, "43 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Vbus(items, 19, "43.b 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            Load(items, 19, "44 复位电子负载CV 0V/0A/0W", 0, 0, 0, false);
            Wait(items, 19, "45 等待1000ms");
            Fixture(items, 19, "46 断开功率路径继电器", Bcm250StationMap.PowerPathRelays, false);
            DaqRelay(items, 19, "46.b 断开VMID/RL6 DAQ301", Bcm250StationMap.MidpointDaqRelay, false);
            Wait(items, 19, "47 等待200ms");
            Action(items, 19, "49 结束仪器串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter20(List<Bcm250ActionDefinition> items)
        {
            Power(items, 20, "1 确认RL4闭合给DUT供电", true);
            Fixture(items, 20, "2 断开功率路径继电器", Bcm250StationMap.PowerPathRelays, false);
            DaqRelay(items, 20, "2.b 断开RL5.12/U1.2 DAQ302", Bcm250StationMap.U12ConnectionDaqRelay, false);
            Wait(items, 20, "3 等待50ms");
            Vbus(items, 20, "4 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            String(items, 20, "4.b 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Load(items, 20, "5 复位电子负载CV 0V/0A/0W", 0, 0, 0, false);
            Fixture(items, 20, "6 闭合String电源Y1/Y3/Y6及放电校准路径RL1a_B/RL1b_B/RL2A",
                Bcm250StationMap.DischargeStringPowerPathRelays, true);
            Wait(items, 20, "7 等待20ms");
            String(items, 20, "8 设置String发生器12V/1A/OVP16", Sk250TestSetpoints.Bcm250.DischargeOneAmp, false);
            String(items, 20, "9 打开String发生器1A输出", Sk250TestSetpoints.Bcm250.DischargeOneAmp, true);
            Action(items, 20, "10 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 20, "11 发送REMOTE2进入BCM250终端", "REMOTE2");
            Mbd(items, 20, "N01", "12-13 平均读取16次R57 @1A", "R57");
            Shunt(items, 20, "N02", "14 DAQ113平均8次测量1A外部分流",
                Bcm250StationMap.StringShuntDaqChannel, Bcm250StationMap.StringShuntOhms);
            String(items, 20, "15 设置String发生器12V/6A/OVP16", Sk250TestSetpoints.Bcm250.DischargeSixAmp, false);
            String(items, 20, "16 打开String发生器6A输出", Sk250TestSetpoints.Bcm250.DischargeSixAmp, true);
            Wait(items, 20, "17 等待2000ms");
            Mbd(items, 20, "N03", "18-19 平均读取16次R57 @6A", "R57");
            Shunt(items, 20, "N04", "20 DAQ113平均8次测量6A外部分流",
                Bcm250StationMap.StringShuntDaqChannel, Bcm250StationMap.StringShuntOhms);
            String(items, 20, "21 关闭String发生器输出", Sk250TestSetpoints.Bcm250.DischargeSixAmp, false);
            String(items, 20, "22 设String发生器0V/0A/OVP10", 0, 0, 10, false);
            Result(items, 20, "N05", "23-26 计算GAINidch",
                Bcm250ActionKind.ComputedCalibration, "N05");
            Result(items, 20, "N06", "23-26 计算OFFSETidch",
                Bcm250ActionKind.ComputedCalibration, "N06");
            Command(items, 20, "27 发送REMOTE2进入BCM250终端", "REMOTE2");
            Action(items, 20, "28 确认N05已写入GAINidch W26",
                Bcm250ActionKind.DocumentedBoundary);
            Action(items, 20, "29 确认N06已写入OFFSETidch W27",
                Bcm250ActionKind.DocumentedBoundary);
            Command(items, 20, "30 保存放电电流校准参数ACT->FLASH", "ACT->FLASH");
            Wait(items, 20, "31 等待3000ms");
            Mbd(items, 20, "N07", "32-34 平均读取50次R45 @0A", "R45");
            String(items, 20, "35 确认String发生器输出关闭", 0, 0, 10, false);
            String(items, 20, "36 设置String发生器12V/6A/OVP16", Sk250TestSetpoints.Bcm250.DischargeSixAmp, false);
            String(items, 20, "37 打开String发生器6A输出", Sk250TestSetpoints.Bcm250.DischargeSixAmp, true);
            Wait(items, 20, "38 等待2000ms");
            Mbd(items, 20, "N08", "39-41 平均读取50次R45 @6A", "R45");
            String(items, 20, "42 关闭String发生器输出", Sk250TestSetpoints.Bcm250.DischargeSixAmp, false);
            String(items, 20, "43 设String发生器0V/0A/OVP10", 0, 0, 10, false);
            Vbus(items, 20, "44 确认VBUS发生器关闭", 0, 0, 300, false);
            String(items, 20, "45 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Vbus(items, 20, "45.b 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            Load(items, 20, "46 复位电子负载CV 0V/0A/0W", 0, 0, 0, false);
            Wait(items, 20, "47 等待1000ms");
            Fixture(items, 20, "48 断开放电校准功率路径", Bcm250StationMap.PowerPathRelays, false);
            Wait(items, 20, "49 等待200ms");
            Command(items, 20, "51 发送EXIT返回MBD终端", "EXIT");
            Action(items, 20, "52 结束仪器串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter21(List<Bcm250ActionDefinition> items)
        {
            Power(items, 21, "1 确认RL4闭合给DUT供电", true);
            Fixture(items, 21, "2 断开功率路径继电器", Bcm250StationMap.PowerPathRelays, false);
            DaqRelay(items, 21, "3 断开RL5.12/U1.2 DAQ302", Bcm250StationMap.U12ConnectionDaqRelay, false);
            Wait(items, 21, "4 等待50ms");
            Vbus(items, 21, "5 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            String(items, 21, "5.b 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Load(items, 21, "6 复位电子负载CV 0V/0A/0W", 0, 0, 0, false);
            Fixture(items, 21, "7 闭合String电源Y1/Y3/Y6及自动放电路径RL1a_B/RL1b_B/RL2A",
                Bcm250StationMap.DischargeStringPowerPathRelays, true);
            Wait(items, 21, "8 等待20ms");
            String(items, 21, "9 设置String发生器5V/0.3A/OVP10", Sk250TestSetpoints.Bcm250.MosfetRampStart, false);
            String(items, 21, "10 打开String发生器输出", Sk250TestSetpoints.Bcm250.MosfetRampStart, true);
            Action(items, 21, "11 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 21, "12 发送REMOTE2进入BCM250终端", "REMOTE2");
            Result(items, 21, "O01", "13 0.3A至1.3A阶梯扫描并记录R45触发电流",
                Bcm250ActionKind.MosfetRamp, "R45");
            Mbd(items, 21, "O02", "13.b 读取R59确认st_gm_sc翻转", "R59");
            String(items, 21, "14 关闭String发生器输出", Sk250TestSetpoints.Bcm250.MosfetRampEnd, false);
            String(items, 21, "15 设String发生器0V/0A/OVP10", 0, 0, 10, false);
            Vbus(items, 21, "16 确认VBUS发生器关闭", 0, 0, 300, false);
            String(items, 21, "17 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Vbus(items, 21, "17.b 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            Load(items, 21, "18 复位电子负载CV 0V/0A/0W", 0, 0, 0, false);
            Wait(items, 21, "19 等待1000ms");
            Fixture(items, 21, "20 断开自动放电功率路径", Bcm250StationMap.PowerPathRelays, false);
            Wait(items, 21, "21 等待200ms");
            Command(items, 21, "23 发送EXIT返回MBD终端", "EXIT");
            Action(items, 21, "24 结束仪器串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter22(List<Bcm250ActionDefinition> items)
        {
            Power(items, 22, "1 确认RL4闭合给DUT供电", true);
            Fixture(items, 22, "2 断开功率路径继电器", Bcm250StationMap.PowerPathRelays, false);
            DaqRelay(items, 22, "3 断开RL5.12/U1.2 DAQ302", Bcm250StationMap.U12ConnectionDaqRelay, false);
            Wait(items, 22, "4 等待50ms");
            Vbus(items, 22, "5 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            String(items, 22, "5.b 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Load(items, 22, "6 复位电子负载CV 0V/0A/0W", 0, 0, 0, false);
            Fixture(items, 22, "7 闭合String电源Y1/Y3/Y6及短路保护功率路径RL1a_B/RL1b_B/RL2A",
                Bcm250StationMap.DischargeStringPowerPathRelays, true);
            DaqRelay(items, 22, "7.b 闭合RL5.12/U1.2 DAQ302", Bcm250StationMap.U12ConnectionDaqRelay, true);
            Wait(items, 22, "8 等待500ms");
            String(items, 22, "9 设置String发生器275V/1A/OVP300", Sk250TestSetpoints.Bcm250.ShortProtection, false);
            String(items, 22, "10 打开String发生器输出", Sk250TestSetpoints.Bcm250.ShortProtection, true);
            Wait(items, 22, "11 等待1000ms");
            Action(items, 22, "12 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 22, "13 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 22, "14 发送DIAG ON到BCM250", "DIAG ON");
            Action(items, 22, "15 设置U1.2短路模拟器2.9V", Bcm250ActionKind.ShortSimulator, "2.9V", true);
            Wait(items, 22, "16 等待500ms");
            Mbd(items, 22, "P01", "17 读取R61确认st_emerg=1", "R61");
            Mbd(items, 22, "P02", "17.b 读取R60确认st_prot_short=1", "R60");
            Wait(items, 22, "18 等待100ms");
            Action(items, 22, "19 设置U1.2短路模拟器3.3V", Bcm250ActionKind.ShortSimulator, "3.3V", true);
            Wait(items, 22, "20 等待500ms");
            Mbd(items, 22, "P03", "21 读取R61确认st_emerg=0", "R61");
            Mbd(items, 22, "P04", "21.b 读取R60确认st_prot_short=0", "R60");
            Action(items, 22, "22 恢复U1.2短路模拟器2.9V", Bcm250ActionKind.ShortSimulator, "2.9V", true);
            String(items, 22, "23 关闭String发生器输出", Sk250TestSetpoints.Bcm250.ShortProtection, false);
            String(items, 22, "24 设String发生器0V/0A/OVP10", 0, 0, 10, false);
            Vbus(items, 22, "25 确认VBUS发生器关闭", 0, 0, 300, false);
            String(items, 22, "26 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Vbus(items, 22, "26.b 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            Load(items, 22, "27 复位电子负载CV 0V/0A/0W", 0, 0, 0, false);
            Wait(items, 22, "28 等待1000ms");
            Fixture(items, 22, "29 断开短路保护功率路径", Bcm250StationMap.PowerPathRelays, false);
            Wait(items, 22, "30 等待200ms");
            DaqRelay(items, 22, "31 断开RL5.12/U1.2 DAQ302", Bcm250StationMap.U12ConnectionDaqRelay, false);
            Command(items, 22, "33 发送EXIT返回MBD终端", "EXIT");
            Action(items, 22, "34 结束仪器串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter23(List<Bcm250ActionDefinition> items)
        {
            Power(items, 23, "1 确认RL4闭合给DUT供电", true);
            Fixture(items, 23, "2 断开功率路径继电器", Bcm250StationMap.PowerPathRelays, false);
            DaqRelay(items, 23, "3 断开RL5.12/U1.2 DAQ302", Bcm250StationMap.U12ConnectionDaqRelay, false);
            Wait(items, 23, "4 等待50ms");
            Vbus(items, 23, "5.a 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            String(items, 23, "5.b 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Load(items, 23, "6 复位电子负载CV 0V/0A/0W", 0, 0, 0, false);
            Fixture(items, 23, "7 闭合String电源Y1/Y3/Y6及充电/VSTR_NEG路径RL1a_A/RL1b_A/RL2B",
                Bcm250StationMap.ChargeStringPowerPathRelays, true);
            Wait(items, 23, "8 等待20ms");
            String(items, 23, "9 设置String发生器0V/0A/OVP300", 0, 0, 300, false);
            Wait(items, 23, "10 等待1000ms");
            String(items, 23, "11 打开String发生器0A输出", 0, 0, 300, true);
            Vbus(items, 23, "12 设置VSTR_NEG 1V/1A/OVP300并输出", Sk250TestSetpoints.Bcm250.VstrNegLow, true);
            Action(items, 23, "13 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 23, "14.a 发送EXIT进入MBD终端", "EXIT");
            Command(items, 23, "14.b 发送DIAG ON到MBD", "DIAG ON");
            Command(items, 23, "15 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 23, "16 发送DIAG ON到BCM250", "DIAG ON");
            Command(items, 23, "17 关闭CCB：W69=1", "W69=1");
            Command(items, 23, "18 设置CCB电流0mA：W12=0", "W12=0");
            Command(items, 23, "19 激活预充电：W75=1", "W75=1");
            Wait(items, 23, "20 等待500ms");
            Mbd(items, 23, "Q01", "21 平均读取4次R50VERB adc0 @0V/0A", "R50VERB");
            Mbd(items, 23, "Q02", "22 平均读取4次R56VERB adc14 @0V/0A", "R56VERB");
            Daq(items, 23, "Q03", "23-24 DAQ109测量V2 @1V/0A", Bcm250StationMap.V2DaqChannel);
            Shunt(items, 23, "Q04", "25 DAQ113测量外部分流电流 @0V/0A",
                Bcm250StationMap.StringShuntDaqChannel, Bcm250StationMap.StringShuntOhms);
            Vbus(items, 23, "26 设置VSTR_NEG 15V/1A/OVP300并输出", Sk250TestSetpoints.Bcm250.VstrNegHigh, true);
            Wait(items, 23, "27 等待2000ms");
            Mbd(items, 23, "Q05", "28 平均读取4次R50 adc0 @15V/0A", "R50");
            Mbd(items, 23, "Q06", "29 平均读取4次R56 adc14 @15V/0A", "R56");
            Daq(items, 23, "Q07", "30-31 DAQ109测量V2 @15V/0A", Bcm250StationMap.V2DaqChannel);
            Vbus(items, 23, "32 设置VSTR_NEG 1V/1A/OVP300并输出", Sk250TestSetpoints.Bcm250.VstrNegLow, true);
            String(items, 23, "33 设置String发生器25V/6A/OVP300并输出", Sk250TestSetpoints.Bcm250.ChargeCalibration, true);
            Wait(items, 23, "34.a 等待7000ms");
            Action(items, 23, "34.b 不执行W69=0（避免打开CCB）", Bcm250ActionKind.DocumentedBoundary);
            Mbd(items, 23, "Q08", "35 平均读取4次R50 adc0 @0V/6A", "R50");
            Mbd(items, 23, "Q09", "36 平均读取4次R56 adc14 @0V/6A", "R56");
            Shunt(items, 23, "Q10", "37 DAQ113测量外部分流电流 @0V/6A",
                Bcm250StationMap.StringShuntDaqChannel, Bcm250StationMap.StringShuntOhms);
            Daq(items, 23, "Q11", "38-39 DAQ109测量V2 @1V/6A", Bcm250StationMap.V2DaqChannel);
            Command(items, 23, "40.a 关闭CCB：W69=1", "W69=1");
            String(items, 23, "40.b 关闭并复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Vbus(items, 23, "41 关闭并复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            Command(items, 23, "42 关闭预充电：W75=0", "W75=0");
            Action(items, 23, "43 检查六个原始校准测量均已通过",
                Bcm250ActionKind.DocumentedBoundary);
            Result(items, 23, "Q12", "44/49 计算gain_vstr_neg并写入W16",
                Bcm250ActionKind.ComputedCalibration, "Q12");
            Result(items, 23, "Q13", "45/49 计算offset_vstr_neg并写入W17",
                Bcm250ActionKind.ComputedCalibration, "Q13");
            Result(items, 23, "Q14", "46/49 计算gain_iccb并写入W22",
                Bcm250ActionKind.ComputedCalibration, "Q14");
            Result(items, 23, "Q15", "47/49 计算gain_iccb_vstr_neg并写入W24",
                Bcm250ActionKind.ComputedCalibration, "Q15");
            Result(items, 23, "Q16", "48/49 计算offset_iccb并写入W23",
                Bcm250ActionKind.ComputedCalibration, "Q16");
            Command(items, 23, "51 保存Q12-Q16校准参数ACT->FLASH", "ACT->FLASH");
            String(items, 23, "52 关闭String发生器输出", 0, 0, 300, false);
            String(items, 23, "53 设String发生器0V/0A/OVP10", 0, 0, 10, false);
            Vbus(items, 23, "54 确认VBUS发生器关闭", 0, 0, 300, false);
            String(items, 23, "55.a 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Vbus(items, 23, "55.b 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            Load(items, 23, "56 复位电子负载CV 0V/0A/0W", 0, 0, 0, false);
            Wait(items, 23, "57 等待1000ms");
            Fixture(items, 23, "58 断开充电/VSTR_NEG功率路径", Bcm250StationMap.PowerPathRelays, false);
            Wait(items, 23, "59.a 等待200ms");
            Command(items, 23, "59.b 发送EXIT返回MBD终端", "EXIT");
            Action(items, 23, "61 结束仪器串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter24(List<Bcm250ActionDefinition> items)
        {
            Power(items, 24, "1 确认RL4闭合给DUT供电", true);
            Fixture(items, 24, "2 断开功率路径继电器", Bcm250StationMap.PowerPathRelays, false);
            Wait(items, 24, "3 等待50ms");
            Vbus(items, 24, "4.a 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            String(items, 24, "4.b 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Load(items, 24, "5 复位电子负载CV 0V/0A/0W", 0, 0, 0, false);
            Fixture(items, 24, "6 闭合String电源Y1/Y3/Y6及验证路径RL1a_A/RL1b_A/RL2B",
                Bcm250StationMap.ChargeStringPowerPathRelays, true);
            Wait(items, 24, "7 等待20ms");
            String(items, 24, "8 设置String发生器0V/0A/OVP300", Sk250TestSetpoints.Bcm250.ChargeVerificationZero, false);
            String(items, 24, "9 打开String发生器输出", Sk250TestSetpoints.Bcm250.ChargeVerificationZero, true);
            Vbus(items, 24, "10 设置VSTR_NEG 1V/1A/OVP300", Sk250TestSetpoints.Bcm250.VstrNegLow, false);
            Vbus(items, 24, "11 打开VBUS发生器输出", Sk250TestSetpoints.Bcm250.VstrNegLow, true);
            Action(items, 24, "12 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 24, "13.a 发送EXIT进入MBD终端", "EXIT");
            Command(items, 24, "13.b 发送DIAG ON到MBD", "DIAG ON");
            Command(items, 24, "14 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 24, "15 发送DIAG ON到BCM250", "DIAG ON");
            Command(items, 24, "16 关闭CCB：W69=1", "W69=1");
            Command(items, 24,
                "17 设置CCB校准目标电流：W12=" + Sk250TestSetpoints.Bcm250.CcbCalibrationCurrentMa,
                "W12=" + Sk250TestSetpoints.Bcm250.CcbCalibrationCurrentMa);
            Command(items, 24, "18 激活预充电：W75=1", "W75=1");
            Wait(items, 24, "19 等待500ms");
            Mbd(items, 24, "R01", "20 读取R42VERB确认vstr_neg @0V/0A", "R42VERB");
            Mbd(items, 24, "R02", "21 读取R44VERB确认iccb @0V/0A", "R44VERB");
            Daq(items, 24, "R03", "22-23 DAQ109测量V2 @0V/0A", Bcm250StationMap.V2DaqChannel);
            Vbus(items, 24, "24 设置VSTR_NEG 1V/1A/OVP300", Sk250TestSetpoints.Bcm250.VstrNegLow, true);
            String(items, 24, "25 设置String发生器15V/6A/OVP300", Sk250TestSetpoints.Bcm250.ChargeVerificationSixAmp, true);
            Wait(items, 24, "26.a 等待5000ms");
            Action(items, 24, "26.b 不执行W69=0（避免打开CCB）", Bcm250ActionKind.DocumentedBoundary);
            Mbd(items, 24, "R04", "27 读取R42 vstr_neg @0V/6A", "R42");
            Mbd(items, 24, "R05", "28 读取R44 iccb @0V/6A", "R44");
            Shunt(items, 24, "R06", "29 DAQ113测量外部分流电流 @0V/6A",
                Bcm250StationMap.StringShuntDaqChannel, Bcm250StationMap.StringShuntOhms);
            Daq(items, 24, "R07", "30-31 DAQ109测量V2 @0V/6A", Bcm250StationMap.V2DaqChannel);
            String(items, 24, "32 关闭并复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Command(items, 24, "33 关闭CCB后再进入15V/0A工作点：W69=1", "W69=1");
            Vbus(items, 24, "34 设置VSTR_NEG 15V/1A/OVP300", Sk250TestSetpoints.Bcm250.VstrNegHigh, true);
            Wait(items, 24, "35 等待1000ms");
            Mbd(items, 24, "R08", "36 读取R42 vstr_neg @15V/0A", "R42");
            Mbd(items, 24, "R09", "37 读取R44 iccb @15V/0A", "R44");
            Daq(items, 24, "R10", "38-39 DAQ109测量V2 @15V/0A", Bcm250StationMap.V2DaqChannel);
            String(items, 24, "40 设置String发生器5V/6A/OVP300", Sk250TestSetpoints.Bcm250.ChargeVerificationFiveVolt, true);
            Command(items, 24, "41 重新打开CCB并等待2000ms：W69=0", "W69=0");
            Mbd(items, 24, "R11", "42 读取R42 vstr_neg @15V/6A", "R42");
            Mbd(items, 24, "R12", "43 读取R44 iccb @15V/6A", "R44");
            Daq(items, 24, "R13", "44-45 DAQ109测量V2 @15V/6A", Bcm250StationMap.V2DaqChannel);
            Shunt(items, 24, "R14", "46 DAQ113测量外部分流电流 @15V/6A",
                Bcm250StationMap.StringShuntDaqChannel, Bcm250StationMap.StringShuntOhms);
            Command(items, 24, "47.a 关闭CCB：W69=1", "W69=1");
            String(items, 24, "47.b 关闭并复位String发生器", 0, 0, 300, false);
            Vbus(items, 24, "48 关闭并复位VBUS发生器", 0, 0, 300, false);
            Command(items, 24, "49.a 关闭预充电：W75=0", "W75=0");
            Command(items, 24, "49.b 发送DIAG ON到BCM250", "DIAG ON");
            Command(items, 24, "49.c 发送EXIT返回MBD终端", "EXIT");
            Action(items, 24, "50 核对R01-R14全部XML限值", Bcm250ActionKind.DocumentedBoundary);
            String(items, 24, "51 关闭String发生器输出", 0, 0, 300, false);
            String(items, 24, "52 设String发生器0V/0A/OVP10", 0, 0, 10, false);
            Vbus(items, 24, "53 确认VBUS发生器关闭", 0, 0, 300, false);
            String(items, 24, "54.a 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Vbus(items, 24, "54.b 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            Load(items, 24, "55 复位电子负载CV 0V/0A/0W", 0, 0, 0, false);
            Wait(items, 24, "56 等待4000ms");
            Fixture(items, 24, "57 断开验证功率路径", Bcm250StationMap.PowerPathRelays, false);
            Wait(items, 24, "58 等待200ms");
            Command(items, 24, "60 发送EXIT返回MBD终端", "EXIT");
            Action(items, 24, "61 结束仪器串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter25(List<Bcm250ActionDefinition> items)
        {
            Power(items, 25, "1 闭合RL4给DUT供电", true);
            Fixture(items, 25, "2 断开功率路径继电器", Bcm250StationMap.PowerPathRelays, false);
            DaqRelay(items, 25, "3 断开RL5.12/U1.2 DAQ302", Bcm250StationMap.U12ConnectionDaqRelay, false);
            Wait(items, 25, "4 等待50ms");
            Vbus(items, 25, "5.a 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            String(items, 25, "5.b 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Load(items, 25, "6 复位电子负载CV 0V/0A/0W", 0, 0, 0, false);
            Fixture(items, 25, "7 闭合String电源Y1/Y3/Y6及CCB路径RL1a_B/RL1b_B/RL2B",
                Bcm250StationMap.CcbStringPowerPathRelays, true);
            Wait(items, 25, "8 等待50ms");
            Action(items, 25, "9 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 25, "10 发送EXIT进入MBD终端", "EXIT");
            Command(items, 25, "11 发送DIAG ON到MBD", "DIAG ON");
            Command(items, 25, "12 闭合MPS RL2：W91=1", "W91=1");
            Vbus(items, 25, "13 设置VBUS发生器275V/10A/OVP300", Sk250TestSetpoints.Bcm250.CcbVbus, false);
            Vbus(items, 25, "14 打开VBUS发生器输出", Sk250TestSetpoints.Bcm250.CcbVbus, true);
            Wait(items, 25, "15 等待15000ms");
            Command(items, 25, "16 发送DIAG ON到MBD", "DIAG ON");
            Command(items, 25, "17 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 25, "18 发送DIAG ON到BCM250", "DIAG ON");
            Command(items, 25, "19 关闭CCB：W69=1", "W69=1");
            Command(items, 25, "20 设置CCB目标0mA：W12=0", "W12=0");
            Command(items, 25, "21 激活预充电：W75=1", "W75=1");
            String(items, 25, "22 设置String发生器225V/0.2A/OVP300", Sk250TestSetpoints.Bcm250.CcbString, false);
            String(items, 25, "23 打开String发生器输出", Sk250TestSetpoints.Bcm250.CcbString, true);
            Wait(items, 25, "24 等待2000ms");
            Load(items, 25, "25 设置电子负载CV模式", 0, 0, 0, false);
            Load(items, 25, "26 设置电子负载250V/7A/2000W", Sk250TestSetpoints.Bcm250.CcbLoad, false);
            Wait(items, 25, "27 等待1000ms");
            Load(items, 25, "28 打开电子负载输入", Sk250TestSetpoints.Bcm250.CcbLoad, true);
            Command(items, 25, "29 设置W11=0", "W11=0");
            Command(items, 25,
                "30 设置CCB电压目标：W13=" + Sk250TestSetpoints.Bcm250.CcbVoltageTargetMv,
                "W13=" + Sk250TestSetpoints.Bcm250.CcbVoltageTargetMv);
            Command(items, 25, "31 关闭MOSFET Q11：W71=0", "W71=0");
            // Hardware test current is intentionally centralized with the other 250-series setpoints.
            Command(items, 25,
                "32 设置CCB硬件测试目标电流：W12=" + Sk250TestSetpoints.Bcm250.CcbHardwareTestCurrentMa,
                "W12=" + Sk250TestSetpoints.Bcm250.CcbHardwareTestCurrentMa);
            Command(items, 25, "33 打开CCB：W69=0", "W69=0");
            Wait(items, 25, "34 等待10000ms");
            Shunt(items, 25, "S01", "35 DAQ113测量CCB外部分流电流",
                Bcm250StationMap.StringShuntDaqChannel, Bcm250StationMap.StringShuntOhms);
            Load(items, 25, "36 负载保持ON并将CV更新为255V", Sk250TestSetpoints.Bcm250.CcbLoadHold, true);
            Wait(items, 25, "37 等待4000ms");
            Daq(items, 25, "S02", "38 DAQ114测量String电压",
                Bcm250StationMap.StringVoltageDaqChannel);
            Command(items, 25, "39 关闭CCB：W69=1", "W69=1");
            Load(items, 25, "40 关闭电子负载", 0, 0, 0, false);
            Load(items, 25, "41 复位电子负载0V/0A", 0, 0, 0, false);
            String(items, 25, "42 关闭String发生器输出", Sk250TestSetpoints.Bcm250.CcbString, false);
            Wait(items, 25, "43 等待7000ms");
            String(items, 25, "44 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Vbus(items, 25, "45 关闭VBUS发生器输出", Sk250TestSetpoints.Bcm250.CcbVbus, false);
            Vbus(items, 25, "46 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            Command(items, 25, "48 关闭预充电：W75=0", "W75=0");
            Fixture(items, 25, "49 断开CCB功率路径", Bcm250StationMap.PowerPathRelays, false);
            Wait(items, 25, "50 等待200ms");
            Command(items, 25, "54 发送EXIT进入MBD终端", "EXIT");
            Command(items, 25, "55 发送DIAG ON到MBD", "DIAG ON");
            Command(items, 25, "56 断开MPS RL2：W91=0", "W91=0");
            Action(items, 25, "57 结束仪器串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter26(List<Bcm250ActionDefinition> items)
        {
            Power(items, 26, "1 确认RL4闭合给DUT供电", true);
            Fixture(items, 26, "2 断开功率路径继电器", Bcm250StationMap.PowerPathRelays, false);
            DaqRelay(items, 26, "3 断开RL5.12/U1.2 DAQ302", Bcm250StationMap.U12ConnectionDaqRelay, false);
            Wait(items, 26, "4 等待50ms");
            Vbus(items, 26, "5.a 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            String(items, 26, "5.b 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Load(items, 26, "6 复位电子负载CV 0V/0A/0W", 0, 0, 0, false);
            Fixture(items, 26, "7 闭合String电源Y1/Y3/Y6及String test路径RL1a_B/RL1b_B/RL2B",
                Bcm250StationMap.CcbStringPowerPathRelays, true);
            Wait(items, 26, "8 等待20ms");
            Vbus(items, 26, "9 设置VBUS发生器100V/0.5A/OVP300", Sk250TestSetpoints.Bcm250.MosTestVbus, false);
            Vbus(items, 26, "10 打开VBUS发生器输出", Sk250TestSetpoints.Bcm250.MosTestVbus, true);
            Wait(items, 26, "11 等待3000ms");
            String(items, 26, "12 设置String发生器90V/3A/OVP300", Sk250TestSetpoints.Bcm250.MosTestString, false);
            String(items, 26, "13 打开String发生器输出", Sk250TestSetpoints.Bcm250.MosTestString, true);
            Action(items, 26, "14 检查USB-B/MBD串口", Bcm250ActionKind.CheckMbdCommunication);
            Command(items, 26, "15 发送EXIT进入MBD终端", "EXIT");
            Command(items, 26, "16 发送DIAG ON到MBD", "DIAG ON");
            Command(items, 26, "17 闭合MPS RL2：W91=1", "W91=1");
            Command(items, 26, "18 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 26, "19 发送DIAG ON到BCM250", "DIAG ON");
            Command(items, 26, "20 关闭CCB：W69=1", "W69=1");
            Command(items, 26, "21 设置CCB目标0mA：W12=0", "W12=0");
            Command(items, 26, "22 打开MOSFET Q11：W71=1", "W71=1");
            Wait(items, 26, "23 等待7000ms");
            Mbd(items, 26, "T01", "24 读取R44内部iccb（MOS打开）", "R44");
            Shunt(items, 26, "T02", "25 DAQ113测量外部分流电流（MOS打开）",
                Bcm250StationMap.StringShuntDaqChannel, Bcm250StationMap.StringShuntOhms);
            Command(items, 26, "26 关闭MOSFET Q11：W71=0", "W71=0");
            Wait(items, 26, "27 等待6000ms");
            Mbd(items, 26, "T03", "28 读取R44内部iccb（MOS关闭）", "R44");
            String(items, 26, "29 关闭String发生器输出", Sk250TestSetpoints.Bcm250.MosTestString, false);
            String(items, 26, "30 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Vbus(items, 26, "31 关闭VBUS发生器输出", Sk250TestSetpoints.Bcm250.MosTestVbus, false);
            Vbus(items, 26, "32 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            String(items, 26, "34.a 再次复位String发生器", 0, 0, 300, false);
            Vbus(items, 26, "34.b 再次复位VBUS发生器", 0, 0, 300, false);
            Load(items, 26, "35 复位电子负载", 0, 0, 0, false);
            Wait(items, 26, "36 等待4000ms");
            Fixture(items, 26, "37 断开String test功率路径", Bcm250StationMap.PowerPathRelays, false);
            Wait(items, 26, "38.a 等待200ms");
            Command(items, 26, "38.b 发送EXIT进入MBD终端", "EXIT");
            Command(items, 26, "39 发送DIAG ON到MBD", "DIAG ON");
            Command(items, 26, "40 断开MPS RL2：W91=0", "W91=0");
            Action(items, 26, "42 结束仪器串口会话", Bcm250ActionKind.DocumentedBoundary);
        }

        private static void AddChapter27(List<Bcm250ActionDefinition> items)
        {
            Power(items, 27, "1 闭合RL4给DUT供电", true);
            Fixture(items, 27, "2 断开功率路径继电器", Bcm250StationMap.PowerPathRelays, false);
            DaqRelay(items, 27, "3 断开RL5.12/U1.2 DAQ302", Bcm250StationMap.U12ConnectionDaqRelay, false);
            Wait(items, 27, "4 等待50ms");
            Action(items, 27, "5 检查VBUS与String发生器COM", Bcm250ActionKind.CheckInstruments);
            Vbus(items, 27, "6.a 复位VBUS发生器0V/0A/OVP300", 0, 0, 300, false);
            String(items, 27, "6.b 复位String发生器0V/0A/OVP300", 0, 0, 300, false);
            Load(items, 27, "7 复位电子负载CV 0V/0A/0W", 0, 0, 0, false);
            Command(items, 27, "8-9 发送REMOTE2进入BCM250终端", "REMOTE2");
            Command(items, 27, "10.a 发送DIAG ON到BCM250", "DIAG ON");
            Command(items, 27, "10.b 写入indice_slave：W38=2", "W38=2");
            Command(items, 27, "10.c 写入identificativo_mcu：W39=MCU_ID", "W39=MCU_ID");
            Command(items, 27, "10.d 清空校准日期：W40=-999", "W40=-999");
            Command(items, 27, "11 清空BCM序列号：W41=-999", "W41=-999");
            Action(items, 27, "12 写入当前校准日期W40", Bcm250ActionKind.WriteIdentity, "R40");
            Action(items, 27, "13 写入扫码BCM序列号W41", Bcm250ActionKind.WriteIdentity, "R41");
            Wait(items, 27, "14 等待300ms");
            Result(items, 27, "U01", "15 保存信息字段ACT->FLASH并核对响应",
                Bcm250ActionKind.StoreToFlash, Bcm250Commands.StoreToFlash);
            Wait(items, 27, "16 等待300ms");
            Result(items, 27, "U02", "17 回读R40核对校准日期",
                Bcm250ActionKind.WriteIdentity, "R40");
            Wait(items, 27, "18 等待500ms");
            Result(items, 27, "U03", "19 回读R41核对BCM序列号",
                Bcm250ActionKind.WriteIdentity, "R41");
            Action(items, 27, "20 先关闭ANS输出再断开RL4", Bcm250ActionKind.SafeAnsPowerOff, Bcm250StationMap.DutPowerRelay.ToString(), false);
            Action(items, 27, "21 结束仪器串口会话", Bcm250ActionKind.DocumentedBoundary);
            Action(items, 27, "22 关闭MPS板电源及全部仪器输出", Bcm250ActionKind.AllOutputsOff);
        }

        private static void AddDetailedCleanup(List<Bcm250ActionDefinition> items)
        {
            AddCleanup(items, "关闭全部电源和电子负载输出", Bcm250ActionKind.AllOutputsOff);
            AddCleanup(items, "全部输出关闭后断开RL4 DUT电源", Bcm250ActionKind.PowerRelay,
                Bcm250StationMap.DutPowerRelay.ToString());
            AddCleanup(items, "断开全部BCM250功率继电器", Bcm250ActionKind.PowerRelays,
                Bcm250StationMap.AllPowerRelays);
            AddCleanup(items, "断开TTL编程夹具继电器", Bcm250ActionKind.FixtureRelays,
                Bcm250StationMap.ProgrammingFixtureRelays);
            AddCleanup(items, "断开DAQ测试继电器301/302/304/305", Bcm250ActionKind.DaqRelays,
                "301,302,304,305");
            AddCleanup(items, "关闭目标板通信并释放治具", Bcm250ActionKind.Cleanup);
        }

        private static void Action(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string name,
            Bcm250ActionKind kind,
            string argument = null,
            bool state = true)
        {
            AddAction(items, chapter, name, kind, argument, state);
        }

        private static void Power(List<Bcm250ActionDefinition> items, int chapter, string name, bool state)
        {
            Action(items, chapter, name, Bcm250ActionKind.PowerRelay,
                Bcm250StationMap.DutPowerRelay.ToString(), state);
        }

        private static void Fixture(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string name,
            string relays,
            bool state)
        {
            Bcm250ActionKind kind = string.Equals(
                relays,
                Bcm250StationMap.ProgrammingFixtureRelays,
                StringComparison.Ordinal)
                ? Bcm250ActionKind.FixtureRelays
                : Bcm250ActionKind.PowerRelays;
            Action(items, chapter, name, kind, relays, state);
        }

        private static void DaqRelay(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string name,
            string relays,
            bool state)
        {
            Action(items, chapter, name, Bcm250ActionKind.DaqRelays, relays, state);
        }

        private static void Command(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string name,
            string command)
        {
            Action(items, chapter, name, Bcm250ActionKind.MbdCommand, command);
        }

        private static void Wait(List<Bcm250ActionDefinition> items, int chapter, string name)
        {
            Action(items, chapter, name, Bcm250ActionKind.Delay);
        }

        private static void Vbus(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string name,
            Sk250SourceSetpoint setpoint,
            bool state)
        {
            Action(items, chapter, name, Bcm250ActionKind.SetVbus,
                setpoint.ToArgument(), state);
        }

        private static void Vbus(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string name,
            double voltage,
            double current,
            double ovp,
            bool state)
        {
            Action(items, chapter, name, Bcm250ActionKind.SetVbus,
                Numbers(voltage, current, ovp), state);
        }

        private static void MpsPower(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string name,
            double voltage,
            double current,
            bool state)
        {
            Action(items, chapter, name, Bcm250ActionKind.SetMpsPower,
                Numbers(voltage, current, 0), state);
        }

        private static void String(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string name,
            Sk250SourceSetpoint setpoint,
            bool state)
        {
            Action(items, chapter, name, Bcm250ActionKind.SetString,
                setpoint.ToArgument(), state);
        }

        private static void String(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string name,
            double voltage,
            double current,
            double ovp,
            bool state)
        {
            Action(items, chapter, name, Bcm250ActionKind.SetString,
                Numbers(voltage, current, ovp), state);
        }

        private static void Load(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string name,
            Sk250SourceSetpoint setpoint,
            bool state)
        {
            Action(items, chapter, name, Bcm250ActionKind.SetLoad,
                setpoint.ToArgument(), state);
        }

        private static void Load(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string name,
            double voltage,
            double current,
            double power,
            bool state)
        {
            Action(items, chapter, name, Bcm250ActionKind.SetLoad,
                Numbers(voltage, current, power), state);
        }

        private static void Mbd(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string resultId,
            string name,
            string command)
        {
            AddResult(items, chapter, resultId, name, Bcm250ActionKind.MbdNumeric, command);
        }

        private static void Daq(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string resultId,
            string name,
            int channel)
        {
            if (string.IsNullOrWhiteSpace(resultId))
                Action(items, chapter, name, Bcm250ActionKind.DaqVoltage, channel.ToString());
            else
                AddDaq(items, chapter, resultId, name, channel);
        }

        private static void Shunt(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string resultId,
            string name,
            int channel,
            double shuntOhms)
        {
            AddShunt(items, chapter, resultId, name, channel, shuntOhms);
        }

        private static void ShuntResistance(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string name,
            int channel,
            double nominalOhms)
        {
            items.Add(new Bcm250ActionDefinition
            {
                Chapter = chapter,
                Name = chapter + ". " + name,
                Kind = Bcm250ActionKind.DaqShuntResistance,
                Channel = channel,
                Scale = nominalOhms,
                State = true
            });
        }

        private static void Result(
            List<Bcm250ActionDefinition> items,
            int chapter,
            string resultId,
            string name,
            Bcm250ActionKind kind,
            string argument)
        {
            AddResult(items, chapter, resultId, name, kind, argument);
        }

        private static string Numbers(double first, double second, double third)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1},{2}",
                first,
                second,
                third);
        }
    }
}
