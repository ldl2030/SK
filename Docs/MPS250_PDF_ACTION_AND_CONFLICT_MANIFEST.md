# MPS-250 Rev.3 PDF action and conflict manifest

Source: `19BU03_012 TESTING PROCEDURE MPS UP250 BOARD rev_3_.pdf`, 36 pages.
Executable procedure chapters are 6-17; threshold table 6 is on pages 20-21.

## Station mapping decisions

- Voltage source A (`HengHuiPower1`) is the VBUS simulator (275 V operating point).
- Voltage source B (`HengHuiPower2`) is the VBATT simulator (275/250 V operating points).
- Ans/source C and the electronic load are not required by this MPS procedure.
- MPS-250 uses no station-3 high-power relays. VBUS source A directly powers
  the MPS board/DUT. Relay_3, Relay_4, Relay_5 and Relay_6 are DAQ relay
  channels 301, 302, 303 and 304. Logical Relay_2 controls both Relay_2 and
  Relay_2BIS on DAQ305/DAQ306.
- Table 9 physical CH01-CH20 maps one-to-one to DAQ101-DAQ120. The latest
  MPS-250 station table puts P+ on DAQ113, VPWM on DAQ114, the 1 ohm VBUS
  absorption shunt on DAQ117 and +5VI on DAQ119; direct-current mode is not used.
- COM_USB uses DAQ111, LS1COM resistance DAQ118, U24 pin 7 DAQ109, P+ as the
  VBUS calibration reference DAQ113, and MCU_ISOBATT/TP230 DAQ105.

## Visible action and result policy

Every command, source setting, output transition, relay operation, wait,
measurement, multi-sample average, calculation, removed-step marker and cleanup
operation is a separate UI row. The 60 selected PDF result codes are unique.
`F8`, `H7`, and `H8` are normalized to `F08`, `H07`, and `H08` for stable IDs.
The four `R71` informational results L01/L05/L09/L13 have no numeric pass range
because table 6 specifies `0..any`.

MPS-250 cleanup sends `W101/W95/W96/W99/W100/W92` only through an already-open
MBD connection. It never rediscovers or recovers USB during cleanup. If fixture
release has started, emergency stop is latched, or the target-board port is
already closed, the visible cleanup row reports that MBD commands were skipped
and continues the hardware power/relay shutdown.

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
| Table 6/REF2 K01 | Earlier extraction `1700..22008 mV` is malformed; approved REF2 shows 1.4-1.9 V | Use approved REF2 value `1400..1900 mV` | Confirm TP230 baseline on a known-good board |
| Table 6/REF2 K02 | `30000..3300 mV` / `30,0..3,3 V` has lower greater than upper | Normalize to `3000..3300 mV` | Confirm TP230 with VBATT=0 V |
| 13.12 | Says H06 must exceed current from point 5/STEP H03, but H03 is a voltage and current baseline is H02 | Compare H06 to H02 and also apply H06 XML range | Confirm expected current increase at W101=200 |
| 13.19 | Says drive zero but printed command is `W101=5` | Safety-confirm with `W101=0` | Verify target firmware zero command |
| 15.3 | Command is printed as `R69VERB`, unlike other procedures' `R69` | Preserve exact Rev.3 `R69VERB` command | Confirm firmware supports verbose query |
| 15.15-16 / table 6 | Detailed text lists J06 Gain_VSTRING from nonexistent PA4 and J07 Gain_VSTRING2; table 6 ends at J06 and only J03/R70 vstring2 exists | Keep one result J06, calculate the sole string gain from J03/R70 (vstring2+) and omit orphan J07 | Confirm calibration variable expected by MPS-250 firmware |
| 8.6 | Gradual ramp has no increment/dwell | 1 V steps, 100 ms dwell, 5-85 V | Approve ramp resolution and dwell |
| G02 | Maximum is infinity | Use lower limit 1000 ohm and no upper limit, rendered as `[1000; any]` | Confirm DAQ over-range representation |
| REF2 vs previous XML thresholds | The supplied approved REF2 uses the chapter-18 values while the previous XML retained several copied/legacy limits, including P+=137-138 V and gain=200k-205k | Use REF2/chapter-18 thresholds in the XML and report definition: P+=270-280 V, gain=400k-410k, and the corresponding B/H/K/L limits | Confirm one known-good MPS-250 against the updated thresholds before Hardware full-flow release |

## Hardware-only gates

Before Hardware mode: continuity/polarity-check DAQ301-306, including the
Relay_2/2BIS pair; confirm source-B VBATT polarity; validate
DAQ118 over-range behavior, DAQ109 U24.7, DAQ113 P+ VBUS and DAQ105 TP230;
approve K01-K03 corrections,
`R69VERB`, J06 calibration semantics, and the ignition ramp. Validate low
voltage first, then one chapter, then the complete flow.
