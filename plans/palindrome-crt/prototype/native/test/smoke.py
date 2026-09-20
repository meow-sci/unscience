#!/usr/bin/env python3
"""ctypes smoke test for the palindrome C ABI shim.

usage: smoke.py <path-to-shared-lib> [--ppm out.ppm] [--fields N] [--colour 0|1] [--lanes N] [--threaded 0|1]
Prints one JSON line per run so results from different builds/hosts can be diffed.
"""
import argparse
import ctypes as C
import json
import sys


class Params(C.Structure):
    _fields_ = [("struct_size", C.c_uint32), ("width", C.c_uint32), ("height", C.c_uint32),
                ("fields", C.c_uint32), ("colour", C.c_uint32), ("deposit_lanes", C.c_uint32),
                ("block_samples", C.c_uint32), ("threaded_pipeline", C.c_uint32),
                ("sample_rate_hz", C.c_double)]


class Result(C.Structure):
    _fields_ = [("struct_size", C.c_uint32), ("hold_locked", C.c_int32),
                ("samples_fed", C.c_uint64), ("accepted_edges", C.c_uint64),
                ("rejected_edges", C.c_uint64), ("detected_fields", C.c_uint64),
                ("field_callbacks", C.c_uint64), ("line_omega", C.c_double),
                ("field_omega", C.c_double), ("agc_gain", C.c_double),
                ("subcarrier_hz", C.c_double), ("burst_amplitude", C.c_double),
                ("killer_gain", C.c_double), ("elapsed_ms", C.c_double),
                ("frame_width", C.c_uint32), ("frame_height", C.c_uint32),
                ("frame_channels", C.c_uint32), ("reserved", C.c_uint32),
                ("frame_bytes", C.c_uint64), ("frame_sum", C.c_uint64),
                ("frame_fnv1a64", C.c_uint64)]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("lib")
    ap.add_argument("--ppm")
    ap.add_argument("--fields", type=int, default=8)
    ap.add_argument("--colour", type=int, default=1)
    ap.add_argument("--lanes", type=int, default=4)
    ap.add_argument("--threaded", type=int, default=0)
    ap.add_argument("--rate", type=float, default=16e6)
    a = ap.parse_args()

    lib = C.CDLL(a.lib)
    lib.pal_abi_version.restype = C.c_uint32
    lib.pal_build_info.restype = C.c_char_p
    lib.pal_last_error.restype = C.c_char_p
    lib.pal_cpu_supported.restype = C.c_int32
    lib.pal_smoke_exception.restype = C.c_int32
    lib.pal_smoke_decode.restype = C.c_int32
    lib.pal_smoke_decode.argtypes = [C.POINTER(Params), C.POINTER(Result), C.c_void_p, C.c_size_t]

    out = {"abi": lib.pal_abi_version(), "build": lib.pal_build_info().decode(),
           "cpu_supported": lib.pal_cpu_supported()}
    rc = lib.pal_smoke_exception()
    out["exception_rc"] = rc
    out["exception_msg"] = lib.pal_last_error().decode()

    p = Params(C.sizeof(Params), 720, 576, a.fields, a.colour, a.lanes, 1 << 16, a.threaded, a.rate)
    r = Result()
    r.struct_size = C.sizeof(Result)
    frame = (C.c_uint8 * (720 * 576 * 3))()
    rc = lib.pal_smoke_decode(C.byref(p), C.byref(r), frame, C.sizeof(frame))
    out["decode_rc"] = rc
    if rc != 0:
        out["error"] = lib.pal_last_error().decode()
    else:
        for name, _ in Result._fields_:
            if name not in ("struct_size", "reserved"):
                v = getattr(r, name)
                out[name] = f"{v:#018x}" if name == "frame_fnv1a64" else v
        if a.ppm:
            ch = r.frame_channels
            with open(a.ppm, "wb") as f:
                f.write(f"P{6 if ch == 3 else 5}\n{r.frame_width} {r.frame_height}\n255\n".encode())
                f.write(bytes(frame[: r.frame_bytes]))
    print(json.dumps(out))
    return 0 if rc == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
