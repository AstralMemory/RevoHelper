# RevoHelper AMD direct-power adapter

RevoHelper does not bundle a kernel driver or silently install one. AMD direct PL testing
therefore requires a compatible, trusted `ryzenadj.exe` placed at this exact relative path:

```text
tools\ryzenadj\ryzenadj.exe
```

The adapter probes the executable with `--info`. It is exposed only when STAPM, SLOW and
FAST limits can be parsed from a real readback. Writes map as follows:

- PL1 -> `--stapm-limit` and `--slow-limit`
- PL2 -> `--fast-limit`

After a write, RevoHelper reads all three values again. A mismatch is treated as failure and
the original limits are restored when possible.

For a non-MECHREVO AMD laptop, use the separately published `RevoHelper.Probe.exe` so OEM
mode switching is not involved:

```powershell
cd .\tools\probe
.\RevoHelper.Probe.exe cpu-power probe
.\RevoHelper.Probe.exe cpu-power apply 35 45 --confirm-direct-write
.\RevoHelper.Probe.exe cpu-power test 35 45 30 --confirm-direct-write
```

`apply` performs a real privileged hardware write for the current boot session. Prefer
`test`: it applies the requested limits for 5-120 seconds and then restores the exact
limits read before the test. Run write tests only while monitoring package power and
temperature, and do not run another tuning application at the same time.
