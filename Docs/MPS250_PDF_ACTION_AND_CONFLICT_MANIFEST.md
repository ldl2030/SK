# MPS-250 Rev.3 PDF action and conflict manifest

Source: `19BU03_012 TESTING PROCEDURE MPS UP250 BOARD rev_3_.pdf`, 36 pages.
Executable procedure chapters are 6-17; threshold table 6 is on pages 20-21.

## Station mapping decisions

- Voltage source A (`HengHuiPower1`) is the VBUS simulator (275 V operating point).
- Voltage source B (`HengHuiPower2`) is the VBATT simulator (275/250 V operating points).
- Ans/source C and the electronic load are not required by this MPS procedure.
- PDF Relay_1 through Relay_6 use 34903A/JAE coil outputs 1-6 and map to
  station high-power relay channels 1-6, not fixture-actuation relays.
- Table 9 physical CH01-CH20 maps to DAQ101-DAQ120. VBUS absorption uses
  physical CH11/DAQ111 across the external 1 ohm shunt; direct-current mode is
  not used.
- LS1COM resistance uses DAQ107. U24 pin 7 uses DAQ108. Gen VBUS reference
  uses DAQ116. MCU_ISOBATT/TP230 uses DAQ117.

## Visible action and result policy

Every command, source setting, output transition, relay operation, wait,
measurement, multi-sample average, calculation, removed-step marker and cleanup
operation is a separate UI row. The 60 selected PDF result codes are unique.
`F8`, `H7`, and `H8` are normalized to `F08`, `H07`, and `H08` for stable IDs.
The four `R71` informational results L01/L05/L09/L13 have no numeric pass range
because table 6 specifies `0..any`.

## Contradictions and selected values

| Location | PDF conflict | Selected implementation | Hardware gate |
|---|---|---|---|
| Chapter 5 title | Says `MPS12580UP` inside the MPS-250 document | Board identity remains MPS-250 from title, scope and all 275/250 V steps | Confirm product selection uses MPS-250 DI bits |
| F8/H7/H8 | Non-zero-padded result labels | Normalize to F08/H07/H08 | None; audit-only rename |
| Table 6 K01 | `1700..22008 mV` is malformed | `1700..2200 mV` | Confirm TP230 baseline on a known-good board |
| Table 6 K02 | `30000..3300 mV` has lower greater than upper | `3000..3300 mV` | Confirm TP230 with VBATT=0 V |
| 13.12 | Says H06 must exceed current from point 5/STEP H03, but H03 is a voltage and current baseline is H02 | Compare H06 to H02 and also apply H06 XML range | Confirm expected current increase at W101=200 |
| 13.19 | Says drive zero but printed command is `W101=5` | Safety-confirm with `W101=0` | Verify target firmware zero command |
| 15.3 | Command is printed as `R69VERB`, unlike other procedures' `R69` | Preserve exact Rev.3 `R69VERB` command | Confirm firmware supports verbose query |
| 15.15-16 / table 6 | Detailed text lists J06 Gain_VSTRING from nonexistent PA4 and J07 Gain_VSTRING2; table 6 ends at J06 and only J03/R70 vstring2 exists | Keep one result J06, calculate the sole string gain from J03/R70 (vstring2+) and omit orphan J07 | Confirm calibration variable expected by MPS-250 firmware |
| 8.6 | Gradual ramp has no increment/dwell | 1 V steps, 100 ms dwell, 5-85 V | Approve ramp resolution and dwell |
| G02 | Maximum is infinity | Executable XML range 1 MOhm to 1 TOhm | Confirm DAQ over-range representation |

## Hardware-only gates

Before Hardware mode: continuity/polarity-check relays 1-6 and Relay_2 dual
contacts; confirm source-B VBATT polarity; validate DAQ107 over-range behavior,
DAQ108 U24.7, DAQ116 Gen VBUS and DAQ117 TP230; approve K01-K03 corrections,
`R69VERB`, J06 calibration semantics, and the ignition ramp. Validate low
voltage first, then one chapter, then the complete flow.
