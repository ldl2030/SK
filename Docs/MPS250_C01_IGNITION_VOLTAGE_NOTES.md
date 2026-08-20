# MPS-250 C01 minimum ignition voltage notes

## Scope and current decision

This note records the MPS-250 Rev.3 chapter 8 minimum ignition voltage test.
It is an engineering follow-up note only. No C# or XML behavior was changed
when this note was created.

PDF requirements:

- Start the VBUS simulator at 5 V, 0.5 A, OVP 300 V.
- Increase VBUS gradually while monitoring the DUT +5V rail.
- Record the VBUS voltage when +5V first exceeds 4.8 V as result C01.
- C01 acceptance range is 0-85 V.

Current software behavior:

- Ramp range: 5-85 V.
- Increment: 1 V.
- Delay after each unsuccessful point: 100 ms, in addition to instrument
  command and DAQ measurement time.
- Detection condition: DAQ101 +5V is greater than 4.8 V.
- Recorded result: the commanded VBUS point in mV.

## Station experience

The expected MPS-250 ignition point is approximately 33 V. Keep the full
5-85 V scan for now. The 33 V value is an engineering expectation, not a new
pass/fail limit and not a replacement for the PDF upper limit of 85 V.

## Future cycle-time optimization entry point

If production data confirms that all good boards ignite in a stable band near
33 V, this test is a candidate for cycle-time reduction. A safe future approach
would remain monotonic and use a coarse pre-scan only below a validated guard
band, followed by a 1 V fine scan across the expected ignition region.

Before changing the ramp, collect at least the following from known-good boards:

- Minimum, maximum and distribution of actual C01 ignition voltages.
- Cold-start versus warm-retest difference.
- Power-supply settling time and DAQ101 response delay near ignition.
- Whether ignition latches after +5V rises, which would make a downward or
  binary-search method invalid without a full power-cycle between probes.

Do not jump directly to 33 V merely because it is the expected value. The
optimized starting point must remain safely below the lowest observed valid
ignition voltage with an agreed production guard margin.

## Deferred software checks

The current ramp returns 85 V when +5V never exceeds 4.8 V. Because 85 V is
also the configured C01 upper limit, that no-ignition path can appear as an
in-range result. When optimization work is authorized, change this path to an
explicit failed result and add a deterministic no-ignition test. This issue is
recorded here only; the current code remains unchanged as requested.

## Change gate

Any later optimization must preserve:

- Current limit 0.5 A and OVP 300 V.
- Monotonic voltage increase and cancellation/emergency-stop handling.
- Detection from the measured +5V signal, not from the source setpoint alone.
- Accurate recording of the first confirmed ignition point.
- Explicit failure when ignition is not detected by 85 V.
- A before/after comparison using known-good and known-failing boards.

