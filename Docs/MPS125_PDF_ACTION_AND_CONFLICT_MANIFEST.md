# MPS-125 Rev.3 PDF action and conflict manifest

Source: `19BU03_020 TESTING PROCEDURE MPS UP125 BOARD rev_3__.pdf`, 36 pages,
procedure chapters 6-17 and threshold table 6 (pages 9-21).

## Station mapping decisions

- Voltage source A (`HengHuiPower1`) is the VBUS simulator.
- Voltage source B (`HengHuiPower2`) is the VBATT simulator. It is not a BCM
  string generator in this sequence.
- Ans/source C and the electronic load are not required by the MPS-125 PDF.
- VBUS source A directly powers the MPS board/DUT. VBATT source B must pass
  through station-3 high-power relays Y1 and Y9; both close before source-B
  output is enabled and open after source-B output is off and discharged.
  PDF Relay_2 through Relay_6 are 34903A/DAQ functional
  relays: Relay_3=`@301`, Relay_4=`@302`, Relay_5=`@303`, Relay_6=`@304`, and
  Relay_2/2BIS=`@305,306`.
- Chapter 6.1 checks only the three physical instruments used by MPS-125:
  voltage source A (VBUS), voltage source B (VBATT), and the DAQ960. The PDF's
  34901A/34903A/34907A module list is retained as functional context, but the
  modules are not treated as three additional serial instruments and are not
  individually identified as a pass condition.
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
their thresholds and units are stored only in XML. Table 6 defines the four R71
rows L01/L05/L09/L13 as `0..any mV`; the XML preserves those document values
without an additional polarity gate or inferred high/low production limits.

Only the two Chapter 15 VBUS restores after Relay_5 switching use the
user-selected ramp `25 -> 50 -> 75 -> 100 -> 125 -> 137.5 V`, with one visible
XML row and a fixed 200 ms wait at every level. These rows do not poll voltage
stability. Chapters 7, 9 and 10 retain the Rev.3 direct 137.5 V startup. Every
VBUS power edge releases the old USB-B serial
handle; after a power cycle the MBD path is rediscovered and validated before
commands resume. Chapter 15 keeps VBUS-off, Relay_5 switching, every ramp
level, MBD recovery, `EXIT`, and `DIAG ON` as separate visible rows.
The visible chapter 8/9/17 communication-close rows now execute instead of
silently returning success.

Chapter 16 prepares K01 by turning VBATT off and safely opening Relay_6.
Relay_6 closes only while source B is off; K02 runs only after the 0 V output
passes the same hardware gate, and K03 runs only after the 125 V output is
stable within 2%. The supply and relay actions remain safety-critical, while
K01/K02/K03 numeric results follow the global failure policy.

Chapter 17 result rows and MBD command rows follow the same global stop-on-fail
configuration as other functional test rows. They do not carry chapter-specific
safety or power-critical overrides. Cleanup still sends MBD safety commands only
on an already-open USB-B connection and never rediscovers a port after fixture
release or emergency stop.

Chapter 17 step 17.1 may rediscover MBD at the chapter boundary. From step 17.2
through the end of the documented stateful heater command chain, the sequence
uses only the current serial connection. If that connection drops, the affected
row fails without re-enumerating into an unknown MCU/DIAG state; whether later
rows run is decided by the global failure policy.

## Contradictions and selected values

| Location | PDF conflict | Selected implementation | Hardware gate |
|---|---|---|---|
| Table 6 K01 | `1700..22008 mV` is inconsistent with the circuit and formatting | `1700..2200 mV` | Confirm TP433 baseline on one known-good board |
| Table 6 K02 | `30000..3300 mV` has lower greater than upper | `3000..3300 mV` | Confirm TP433 with VBATT=0 V |
| 13.13/13.14 | Text requires PWM 200, printed commands remain `W101=700`/`W295=700` | `W101=200`, `W295=200`, supported by H07/H08 2.9-3.1 V thresholds | Observe U20.7/U20.5 at low drive before full run |
| 13.26 | Text says set drive to zero but command is `W101=5` after prior shutdown | Safety-confirm `W101=0` | Verify target firmware accepts zero command |
| 16.4/16.5 | Text names RELAY_6 and parenthetically `ex 7` | Current Rev.3 table 8 CH_4 / DAQ304 functional relay | Continuity-check DAQ304 Relay_6 to J7.5 |
| 8.6 | “Gradually” gives no increment or dwell | 1 V increments, 100 ms dwell, 5-80 V | Approve ramp resolution and dwell at station |
| Chapter 15 VBUS restores | Rev.3 applies 137.5 V directly and gives no inrush-control ramp | User-selected safety override only for the two Relay_5 restores: `25/50/75/100/125/137.5 V`, fixed 200 ms per level, no stability polling; Chapters 7/9/10 remain direct | Validate first on one known-good MPS-125 board before a complete Hardware-mode run |
| G02 | Maximum is infinity | XML uses 1 MOhm to 1 TOhm as an executable open-contact range | Confirm DAQ over-range representation |
| Table 6 L01/L05/L09/L13 | `0..any mV` does not define a high/low functional limit | Preserve the authoritative Rev.3 range `0..any mV`; record each state value without adding an inferred polarity gate | Review recorded values if a future controlled document revision introduces state limits |
| PDF table 9 vs current station table | Older implementation used COM_USB=CH05, shunt=CH11, P+=CH04, MCU_ISOBATT=CH17, LSI.COM=CH07 and Gen VBUS=CH16 | Latest user-provided MPS-125UP table: COM_USB=CH11, shunt=CH17, P+=CH13, MCU_ISOBATT=CH05, LSI.COM=CH18; CH16 is empty | Continuity-check CH05/11/13/17/18 before the first Hardware-mode run |

## Hardware-only gates

Before Hardware mode: confirm DAQ301-306 relay polarity, continuity for Relay_2
dual contacts, DAQ118 resistance over-range behavior, source-B VBATT polarity,
TP433 K01-K03 limits, CH05/11/13/17/18 continuity, USB-B insertion/probing,
the 1 V/100 ms ignition ramp, and the two Chapter 15 six-level 25-137.5 V restore ramps.
Validate first at low voltage, then one selected chapter, then the complete
flow.
