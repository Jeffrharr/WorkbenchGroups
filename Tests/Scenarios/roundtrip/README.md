# roundtrip/

These two scenarios are a pair, and they are kept out of `Tests/Scenarios/` for one reason: a
scenario names its fixture, and **every scenario in one run must share one**. `reload_roundtrip_load`
names the save that `reload_roundtrip_save` writes, so globbing the parent directory would sweep it
into a suite that boots the wrong fixture — and it would fail exactly the way a real regression
does, on every probe, which is the worst kind of noise.

Run them with `Tests/run_roundtrip.sh`, which does the two runs and carries the save between them.
`Tests/Scenarios/*.json` stays a valid one-load suite.
