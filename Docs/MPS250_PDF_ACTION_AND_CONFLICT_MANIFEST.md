# MPS-250 Rev.3 PDF action and conflict manifest

Source: `19BU03_012 TESTING PROCEDURE MPS UP250 BOARD rev_3_.pdf`, 36 pages.
Executable procedure chapters are 6-17; threshold table 6 is on pages 20-21.

## Station mapping decisions

- Voltage source A (`AinuoPowerA`, AN50300-10) is the VBUS simulator (275 V operating
  point); voltage and current limits are sent separately and remote OVP is not sent.
- Voltage source B (`HengHuiPower2`) is the VBATT simulator (275/250 V operating points).
- Ans/source C and the electronic load are not required by this MPS procedure.
- VBUS source A directly powers the MPS board/DUT. VBATT source B must pass
  through station-3 high-power relays Y1 and Y9; both close before source-B
  output is enabled and open after source-B output is off and discharged.
  Relay_3, Relay_4, Relay_5 and Relay_6 are DAQ relay
  channels 308, 302, 303 and 304. Logical Relay_2 controls both Relay_2 and
  Relay_2BIS on DAQ305/DAQ306.
- Table 9 physical CH01-CH20 maps one-to-one to DAQ101-DAQ120. The latest
  MPS-250 station table puts P+ on DAQ113, VPWM on DAQ114, the 1 ohm VBUS
  absorption shunt on DAQ117 and +5VI on DAQ119; direct-current mode is not used.
- COM_USB uses DAQ111, LS1COM resistance DAQ118, U24 pin 7 DAQ109, P+ as the
  VBUS calibration reference DAQ113, and MCU_ISOBATT/TP230 DAQ105.
- User reconfirmed the measurement assignments (DAQ101/102/103/105/106/107/
  108/109/111/113/114/117/118/119/120) and the original relay wiring on
  2026-08-20. Per the 2026-10-10 user update, Relay_3 now uses CH_8/DAQ308; damaged CH_1/DAQ301 remains excluded, and Relay_2 and Relay_4-6 retain their assignments.
- MPS-250 USB-B follows Rev.3 chapter 9 at 115200 baud, 8N1, no flow control,
  transmit CR. BCM keeps its separate station-confirmed 9600 baud setting.

## Visible action and result policy

Every command, source setting, output transition, relay operation, wait,
measurement, multi-sample average, calculation, removed-step marker and cleanup
operation is a separate UI row. The 60 selected PDF result codes are unique.
`F8`, `H7`, and `H8` are normalized to `F08`, `H07`, and `H08` for stable IDs.
Table 6 leaves the four `R71` results L01/L05/L09/L13 effectively unchecked as
`0..any`. After the 2026-08-21 failed run showed all four stuck at zero while
every command still returned `OK`, they are now treated as functional state
checks. The selected ranges follow the known-good report pattern: L01/L09/L13
high=`3900..4096 Count`, L05 low=`0..100 Count`.

MPS-250 cleanup sends `W101/W95/W96/W99/W100/W92` only through an already-open
MBD connection. It never rediscovers or recovers USB during cleanup. If fixture
release has started, emergency stop is latched, or the target-board port is
already closed, the visible cleanup row reports that MBD commands were skipped
and continues the hardware power/relay shutdown.

The final cleanup row closes target-board communication, opens the MPS-250
fixture press-down command through station-1 Y1, and verifies that DI7 reports
the released position. MPS chapter-failure handling is board-specific: it does
not operate station-3 Relay_1, BCM programming relays, or BCM `W86` commands.

During every MPS-250 test, a board-specific background monitor records both
HengHui 8602 sources to the persistent daily live log. The default one-second
record contains channel/SN, VBUS source-A and VBATT source-B output ON/OFF,
measured voltage and measured current. Telemetry read failures are recorded but
do not change the product result; short read timeouts prevent logging from
materially delaying emergency output shutdown.

Before B01 and D01, the corresponding VBUS output-on row now waits for source-A
measured voltage to stabilize instead of immediately taking a DAQ reading. The
default gate waits at least 500 ms, then requires three consecutive readings
within 2% of 275 V at 250 ms intervals, with a 5 s maximum. A timeout fails the
power-critical output-on row, preventing transient ramp values from being used
as B01/D01 product results.

Chapter 16 now prepares K01 by turning VBATT off, safely opening Relay_6 and
requiring three consecutive source-B readbacks within 0.5 V of zero (and below
0.05 A). Relay_6 is then closed only while source B is off; K02 is allowed only
after the 0 V output passes the same readback gate. K03 is allowed only after
the 250 V output is stable within 2%. These gates are safety-critical and
cannot be bypassed by the engineering continue-after-power-failure option.

DAQ series timing is start-to-start: a slow `READ?` no longer receives an
additional full interval delay. If the serial acquisition itself exceeds the
requested interval, sampling continues immediately and logs one overrun. This
avoids extra waits after slow serial acquisitions. K01-K03 use the DAQ internal
timed scan with 81 endpoint-inclusive readings at 100 ms over a real 8-second
window. Final Chapter 16/17 result failures follow the configured global
stop-on-fail policy; their power transitions retain the separate safety gates.

The user authorized shorter averages on 2026-10-10. MPS-250 Chapter 11/13/17
16-sample averages now use `SKMPS250PwmAverageSampleCount` (default/minimum 8;
16 restores the procedure count), retaining 100 ms intervals and NPLC=1.
Fixed DC ranges are selected per result state rather than globally per channel,
including the three-sample Chapter 14 averages. K01-K03 timed windows and the
10-second heater settling waits are unchanged. Timing evidence, ranges, and
estimated savings are recorded in `Docs/MPS250_145249_测试提速分析_20261010.md`.

## Main-switch terminology

The Rev.3 procedure exposes one MCU main-switch drive on MPS-250:
`drv_main_sw PE14`, controlled by `W101`. The two heater PWM channels are
`risc1 PD13` / `W99` and `risc2 PD14` / `W100`; they are not Main_SW1/Main_SW2.
The fixture inventory lists two relay parts for main-switch simulation, while
Figure 1 BIS represents their external function as one coil simulator across
J14.1/J14.2. DI9 `Main_SW1` and DI10 `Main_SW2` are two digital feedback inputs
from the fixture. The available station table does not provide enough pin/contact
mapping to claim either input is identical to the `W101` command. Treat them as
feedback in the same functional area, not as the MCU control command, until
continuity or fixture schematic confirmation identifies the two contacts.

After the common fixture press-down row, `MPS250.PRECHECK.S009.ACTION` prompts
the operator to insert USB-B and confirm the physical connection. This happens
before Chapter 6 and therefore before VBUS/D01. Because the USB-A serial adapter
stays connected to the PC, inserting the USB-B device end does not enumerate a
new COM port. MPS-250 therefore clears historical/fixed candidates (including
COM25) and, after DUT power/boot, probes all existing unoccupied COM ports for
the strict MBD `COMMAND ERROR` + `Ready.` response. MPS-250 never accepts a port
merely because it is the only candidate. BCM-250 keeps its board-specific
configured/fixed-port behavior.

## Contradictions and selected values

| Location | PDF conflict | Selected implementation | Hardware gate |
|---|---|---|---|
| Chapter 5 title | Says `MPS12580UP` inside the MPS-250 document | Board identity remains MPS-250 from title, scope and all 275/250 V steps | Confirm product selection uses MPS-250 DI bits |
| F8/H7/H8 | Non-zero-padded result labels | Normalize to F08/H07/H08 | None; audit-only rename |
| Table 6 K01 | Printed `1700..22008 mV` contains an extra trailing digit; the legacy report renders the intended maximum as 2200 mV | Use corrected PDF intent `1700..2200 mV` | Confirm TP230 baseline on a known-good board |
| Table 6/REF2 K02 | `30000..3300 mV` / `30,0..3,3 V` has lower greater than upper | Normalize to `3000..3300 mV` | Confirm TP230 with VBATT=0 V |
| Table 6 L01/L05/L09/L13 | `0..any` cannot detect a stuck R71 signal | Use known-good state pattern: high 3900-4096 for L01/L09/L13 and low 0-100 for L05 | Confirm R71 high/low polarity on one known-good board |
| 13.12 | Says H06 must exceed current from point 5/STEP H03, but H03 is a voltage and current baseline is H02 | Compare H06 to H02 and also apply H06 XML range | Confirm expected current increase at W101=200 |
| 13.19 | Says drive zero but printed command is `W101=5` | Safety-confirm with `W101=0` | Verify target firmware zero command |
| 15.3 | Command is printed as `R69VERB`, unlike other procedures' `R69` | Preserve exact Rev.3 `R69VERB` command | Confirm firmware supports verbose query |
| 15.15-16 / table 6 | Detailed text lists J06 Gain_VSTRING from nonexistent PA4 and J07 Gain_VSTRING2; table 6 ends at J06 and only J03/R70 vstring2 exists | Keep one result J06, calculate the sole string gain from J03/R70 (vstring2+) and omit orphan J07 | Confirm calibration variable expected by MPS-250 firmware |
| 8.6 | Gradual ramp has no increment/dwell | 1 V steps, 100 ms dwell, 5-85 V | Approve ramp resolution and dwell |
| G02 | Maximum is infinity | Use PDF table-6 lower limit 1000000 ohm and no upper limit, rendered as `[1000000; any]` | Confirm DAQ over-range representation |
| Table 6 §18 J04/J05/J06 | Printed gain=200k-205k is incorrect; the 2026-10-10 hardware log gives J05=404586.69 | User corrected all three gains to 400000-410000 on 2026-10-10; XML and generated reports use this range | Gain steps reuse the successful DAQ113 reference from 15.12; a missing/failed reference blocks calculation |
| Table 6 L02-L16, excluding any rows | Printed heater thresholds differ from the user-provided 2026-10-10 image | Apply the image's twelve finite L ranges; retain L01/L05/L09/L13 exactly as requested | Validate actual heater behavior on the next hardware run |
| Chapter 11/13/17 averages | Procedure specifies 16 readings; user authorized reducing unnecessary sampling time on 2026-10-10 | Default eight readings with fixed ranges and independent result judgments; configurable return to 16 | Confirm average stability and cycle-time savings on the next hardware run |

## Hardware-only gates

C01 cycle-time observations and the future optimization gate are recorded in
`Docs/MPS250_C01_IGNITION_VOLTAGE_NOTES.md`. The expected ignition point is
approximately 33 V; the current 5-85 V implementation remains unchanged.

The DAQ302-306/308 relay assignments, Relay_2/2BIS pairing, and listed DAQ101-120
signal assignments are station-confirmed and do not block Hardware mode.
During first hardware execution, validate relay contact polarity/behavior,
source-B VBATT polarity, DAQ118 over-range behavior, DAQ109 U24.7, DAQ113 P+
VBUS and DAQ105 TP230;
approve K01-K03 corrections,
`R69VERB`, J06 calibration semantics, and the ignition ramp. Validate low
voltage first, then one chapter, then the complete flow.

## 2026-08-20 Relay_5 / DAQ303 hardware incident

Hardware run SN `213123` reached Chapter 15 after the engineering override
allowed the earlier G01 power-critical failure to continue. G01 and G02 both
read DAQ118 over-range/open-circuit; G01 therefore failed its 0-5 ohm closed
limit while the same reading passed G02's >=1 Mohm open limit. G02 passing does
not prove the DAQ118 harness or LS1 contact path is connected.

At Chapter 15 step 15.4, the implementation turned VBUS off, waited until its
readback fell below the configured safe threshold, closed Relay_5 on DAQ303,
and restored 275 V. The confirmed fixture table connects DAQ303 COM to DUT
J18.3 `SENS PREC`, N.O. to source-A interface A.1 `VBUS+`, and leaves N.C.
unwired. About 0.8 seconds after restoring VBUS, the normalized DI8 emergency
input asserted and the hardware safety path removed power; a DUT component was
reported smoking.

The timing identifies closing Relay_5 as the operation that applied the final
stress to the `SENS PREC` path, but it does not by itself prove that the DAQ303
relay is defective. Before the DAQ303 operation, source-A telemetry already
reported approximately 275 V / 0.42 A, far above the documented Relay_5 branch
load of 0.6 mA and inconsistent with the B06 10-60 mA shunt limit. Quarantine
the damaged DUT. Before another 275 V run, verify DAQ303 COM/N.O. continuity
and isolation with the DUT removed, confirm A.1 and J18.3 harness endpoints,
compare the DUT `SENS PREC` input impedance with a known-good board, and
reconcile DAQ117 shunt current with the source-A current readback. Do not use
the power-critical continuation override for the next hardware validation.
