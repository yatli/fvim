Input Latency Benchmark
-----------------------

This is a benchmark to test the input response time.
The experiment is done with the iPhone app "Is it snappy?", and thus end-to-end (photons to photons, anyway)

The latency is measured by recording the time between a `j` keystroke on a keyboard with right index finger, and the first frame that a sign of updated buffer is reflected on my screen.
The key frames are picked manually by stepping through the single frames.
My iPhone7 is capable of 120fps recording, and thus ~8.33ms per frame.

instance    time        frames
------------------------------
nvim-qt     75.0ms      9
nvim-qt     58.3ms      7
conhost     83.3ms      10
fvim        100.0ms     12
fvim        116.7ms     14
fvim        116.7ms     14
nvim-gtk    83.3ms      10

Looks like `fvim` is constantly lagging behind for a few frames , and in the worst case, 2x less responsive than `nvim-qt`.
The tests are done on Windows10, with immediate renderer (and thus better latency) -- this means that the readings on X11 will be even worse.
