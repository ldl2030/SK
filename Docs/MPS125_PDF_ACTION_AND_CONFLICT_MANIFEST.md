# MPS-125 Rev.3 PDF action and conflict manifest

Source: `19BU03_020 TESTING PROCEDURE MPS UP125 BOARD rev_3__.pdf`, 36 pages,
procedure chapters 6-17 and threshold table 6 (pages 9-21).

## Station mapping decisions

- Voltage source A (`HengHuiPower1`) is the VBUS simulator.
- Voltage source B (`HengHuiPower2`) is the VBATT simulator. It is not a BCM
  string generator in this sequence.
- Ans/source C and the electronic load are not required by the MPS-125 PDF.
- MPS-125 uses no station-3 high-power relays. VBUS source A directly powers
  the MPS board/DUT. PDF Relay_2 through Relay_6 are 34903A/DAQ functional
  relays: Relay_3=`@301`, Relay_4=`@302`, Relay_5=`@303`, Relay_6=`@304`, and
  Relay_2/2BIS=`@305,306`.
- The newest user-provided fixture table supersedes PDF table 9 for physical
  CH01-CH20. VBUS current uses the external 1 ohm shunt on physical
  CH17/DAQ117. No direct-current DAQ mode is used.
- LSI.COM resistance uses physical CH18/DAQ118. The independent reference for
  J05-J08 uses P+ on physical CH13/DAQ113; physical CH16 is empty and must not
  be measured.
- MPS-125 has the same manual USB-B insertion workflow as MPS-250: prompt after
  fixture press-down, clear fixed/historical MBD candidates, then probe existing
  unoccupied COM ports after DUT power and boot wait.

## Visible action policy

Every document command, source setting/output transition, relay change, wait,
measurement, calculation, removed-step marker, chapter boundary, and cleanup
operation is a separate catalog/XML row. The 70 PDF result codes remain unique;
their thresholds and units are stored only in XML. Four informational R71 rows
(L01/L05/L09/L13) retain no numeric pass range because table 6 says `0..any`.

## Contradictions and selected values

| Location | PDF conflict | Selected implementation | Hardware gate |
|---|---|---|---|
| Table 6 K01 | `1700..22008 mV` is inconsistent with the circuit and formatting | `1700..2200 mV` | Confirm TP433 baseline on one known-good board |
| Table 6 K02 | `30000..3300 mV` has lower greater than upper | `3000..3300 mV` | Confirm TP433 with VBATT=0 V |
| 13.13/13.14 | Text requires PWM 200, printed commands remain `W101=700`/`W295=700` | `W101=200`, `W295=200`, supported by H07/H08 2.9-3.1 V thresholds | Observe U20.7/U20.5 at low drive before full run |
| 13.26 | Text says set drive to zero but command is `W101=5` after prior shutdown | Safety-confirm `W101=0` | Verify target firmware accepts zero command |
| 16.4/16.5 | Text names RELAY_6 and parenthetically `ex 7` | Current Rev.3 table 8 CH_4 / DAQ304 functional relay | Continuity-check DAQ304 Relay_6 to J7.5 |
| 8.6 | “Gradually” gives no increment or dwell | 1 V increments, 100 ms dwell, 5-80 V | Approve ramp resolution and dwell at station |
| G02 | Maximum is infinity | XML uses 1 MOhm to 1 TOhm as an executable open-contact range | Confirm DAQ over-range representation |
| PDF table 9 vs current station table | Older implementation used COM_USB=CH05, shunt=CH11, P+=CH04, MCU_ISOBATT=CH17, LSI.COM=CH07 and Gen VBUS=CH16 | Latest user-provided MPS-125UP table: COM_USB=CH11, shunt=CH17, P+=CH13, MCU_ISOBATT=CH05, LSI.COM=CH18; CH16 is empty | Continuity-check CH05/11/13/17/18 before the first Hardware-mode run |

## Hardware-only gates

Before Hardware mode: confirm DAQ301-306 relay polarity, continuity for Relay_2
dual contacts, DAQ118 resistance over-range behavior, source-B VBATT polarity,
TP433 K01-K03 limits, CH05/11/13/17/18 continuity, USB-B insertion/probing,
and the 1 V/100 ms ignition ramp. Validate first at low voltage, then one
selected chapter, then the complete flow.
