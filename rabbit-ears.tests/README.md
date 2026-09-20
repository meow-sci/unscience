# rabbit-ears.tests

Console checks for [`rabbit-ears`](../rabbit-ears/README.md) (no KSA assemblies, no PALindrome needed). Exit code 0 means
every check passed.

```sh
dotnet run --project rabbit-ears.tests
```

| File | Checks |
|---|---|
| `EncoderChecks.cs` | 1280 samples/line, 625 lines, one frame per 800000 samples; sync tip -0.3 V, white +0.7 V, broad / equalising / half-line structure; burst amplitude 0.15 V and burst phase against one unbroken subcarrier reference across three frames; chroma amplitude / phase / luma for saturated red; bit-identical output for block sizes 1 ... 160000; frame resampling |
| `SpectrumChecks.cs` | FFT scaling; VSB filter design targets; two-channel mux spectrum for K = 4, 6, 8: carriers at f_k and at the right level, lower vestige >= 20 dB down at -1.25 MHz, double-sideband inside +/-0.75 MHz, out-of-slot leakage <= -40 dB of the sideband level, interpolation images of the carriers <= -70 dBc |
| `StreamingChecks.cs` | block-size invariance of modulator + placer, mux and tuner (with noise, odd block sizes); u8 / s16 file round trips with and without TPDF dither, sidecar fields, SigMF output; NCO accuracy |
| `TunerChecks.cs` | for every slot of a full mux (K = 4, 6, 8): tuner + a simple quadrature envelope detector recovers the 2560-sample line period, the tip / blanking levels and *that slot's* picture level; tone lands at 8 MHz with unity gain, retune moves it without a discontinuity; receiver-noise level and Gaussianity |
| `NetChecks.cs` | sender contract on the wire: `u8 = round((v + 0.42) x 182)` and `s16 = round(v x 24000)` for the PAL levels, clipping, round trips within half an LSB, wire -> FIFO scale; hello / reply / block-header layout and round trips (UTF-8 name, any-slot), rejection of bad magic, version, format, truncation and absurd block sizes |
| `SenderChecks.cs` | `SenderServer` over a real TCP loopback with `SenderClient`: refusal of locally held, out-of-range and already-connected slots, first-free-slot assignment; a grey programme is streamed, starved and resumed and every transmitted sample must equal the unbroken grey stream or the unbroken black "no programme" stream at that position (so levels, sync, V switch and burst phase never jump), the programme resumes exactly on the 8-field boundary, one underrun is counted; disconnect is noticed and the slot can be claimed again |
| `LiveChecks.cs` | live pipeline without PALindrome: paced `MuxProducer` -> `Tuner` -> counting sink for 0.5 s: never ahead of the wall clock, every block delivered as 40 MS/s s16 at the expected level, producer -> tuner queue bounded, a channel added while running is picked up between blocks, spectrum snapshot, clean shutdown |
