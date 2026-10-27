/*++
    PrecisionCurve - host-side unit tests for the shared math

    Build & run:  make -C tests
    Requires a host C compiler only (no Windows, no WDK).
--*/

#include <stdio.h>
#include <stdlib.h>
#include <math.h>

#include "../shared/precisioncurve_math.h"
#include "../shared/precisioncurve_curves.h"

static int g_failures;

#define CHECK(cond, ...)                                        \
    do {                                                        \
        if (!(cond)) {                                          \
            g_failures++;                                       \
            printf("FAIL %s:%d: ", __FILE__, __LINE__);         \
            printf(__VA_ARGS__);                                \
            printf("\n");                                       \
        }                                                       \
    } while (0)

static void test_isqrt(void)
{
    unsigned long long v;

    CHECK(PcISqrt64(0) == 0, "isqrt(0) = %u", PcISqrt64(0));
    CHECK(PcISqrt64(1) == 1, "isqrt(1) = %u", PcISqrt64(1));
    CHECK(PcISqrt64(144) == 12, "isqrt(144) = %u", PcISqrt64(144));

    for (v = 0; v < 200000; v++) {
        pc_u32 got = PcISqrt64(v);
        pc_u32 want = (pc_u32)sqrt((double)v);

        if (got != want) {
            g_failures++;
            printf("FAIL isqrt(%llu): got %u want %u\n", (unsigned long long)v, got, want);
            return;
        }
    }

    /* Large values: check the invariant root^2 <= v < (root+1)^2 */
    for (v = 1; v < 20000; v++) {
        pc_u64 big = v * 1234567890123ull;
        pc_u64 root = PcISqrt64(big);

        CHECK(root * root <= big && (root + 1) * (root + 1) > big,
              "isqrt invariant failed for %llu", (unsigned long long)big);
    }
}

static void test_vec_len(void)
{
    /* 3-4-5 triangle in counts -> length 5 counts in Q16 */
    /* 300/400 counts is a 500 count vector */
    CHECK(PcVecLenQ16FromRaw(300, 400) == 500 * PC_Q16_ONE,
          "3-4-5 length = %d", PcVecLenQ16FromRaw(300, 400));
    CHECK(PcVecLenQ16FromRaw(0, 0) == 0, "zero vector length");
    CHECK(PcVecLenQ16FromRaw(-300, -400) == 500 * PC_Q16_ONE, "negative vector");

    /* 32767/32767 must not overflow */
    {
        pc_i32 len = PcVecLenQ16FromRaw(32767, 32767);
        /* sqrt(2)*32767 counts, in Q16: ~3.03e9, i.e. above int32 -> saturates */
        CHECK(len == 0x7FFFFFFF, "diagonal length did not saturate: %d", len);
    }
}

static void test_axis_gain(void)
{
    /* Unity gain must be a perfect passthrough for every delta. */
    pc_i32 delta = -32767;
    int i;

    for (i = 0; i < 5000; i++, delta++) {
        pc_i32 residual = 0;
        pc_i32 out = PcApplyAxisGain(delta, PC_GAIN_UNITY_Q16, &residual);

        CHECK(out == delta, "unity gain changed %d into %d", delta, out);
        CHECK(residual == 0, "unity gain left residual %d", residual);
    }

    /* 0.5x gain halves, and the accumulated error stays below one count. */
    {
        pc_i32 residual = 0;
        long long sum = 0;
        int n = 1000;

        for (i = 0; i < n; i++) {
            sum += PcApplyAxisGain(1, PC_Q16_ONE / 2, &residual);
        }
        CHECK(llabs(sum - n / 2) <= 1,
              "0.5x of 1000 unit steps produced %lld", sum);
        CHECK(abs(residual) <= PC_Q16_ONE, "residual escaped: %d", residual);
    }

    /* 2.0x gain doubles and leaves no drift. */
    {
        pc_i32 residual = 0;
        long long sum = 0;
        int n = 1000;

        for (i = 0; i < n; i++) {
            sum += PcApplyAxisGain(1, 2 * PC_Q16_ONE, &residual);
        }
        CHECK(sum == 2 * n, "2.0x of %d unit steps produced %lld", n, sum);
    }

    /* Extreme gain must stay inside int32 and never trap. */
    {
        pc_i32 residual = 0;
        pc_i32 out = PcApplyAxisGain(32767, PC_GAIN_MAX_Q16, &residual);
        CHECK(out > 0, "max gain clamp produced %d", out);
    }
}

static void test_lut(void)
{
    PC_CURVE_PARAMS p = PcPresets[1].Params; /* FPS Flick */
    pc_i32 lut[256];
    pc_u32 maxQ16 = 0;
    pc_u32 scale;
    pc_u32 i;

    CHECK(PcBuildLut(&p, lut, 256, &maxQ16) > 0, "PcBuildLut failed");
    CHECK(maxQ16 == (pc_u32)(p.MaxSpeed * 65536.0 + 0.5), "maxSpeedQ16 = %u", maxQ16);

    for (i = 1; i < 256; i++) {
        CHECK(lut[i] >= lut[i - 1], "LUT not monotonic at %u", i);
    }
    CHECK(lut[0] == PcGainToQ16(p.Base), "lut[0] = %d, base gain = %d",
          lut[0], PcGainToQ16(p.Base));

    scale = PcLutScaleQ32(256, maxQ16);
    CHECK(scale != 0, "scale is zero");

    /* Endpoints and clamping. */
    CHECK(PcLutSample(lut, 256, scale, 0) == lut[0], "sample at 0");
    CHECK(PcLutSample(lut, 256, scale, (pc_i32)maxQ16) == lut[255], "sample at max");
    CHECK(PcLutSample(lut, 256, scale, (pc_i32)maxQ16 * 4) == lut[255],
          "sample beyond max must hold the last knot");
    CHECK(PcLutSample(lut, 256, scale, -1000) == lut[0], "negative speed");

    /* Monotonic in speed. */
    for (i = 0; i < 400; i++) {
        pc_i32 s = (pc_i32)((pc_u64)i * (pc_u64)maxQ16 / 400);
        pc_i32 g0 = PcLutSample(lut, 256, scale, s);
        pc_i32 g1 = PcLutSample(lut, 256, scale, s + 1000);

        CHECK(g1 >= g0, "gain decreased between %d and %d", s, s + 1000);
        CHECK(g0 >= PC_GAIN_MIN_Q16 && g0 <= PC_GAIN_MAX_Q16,
              "gain out of bounds: %d", g0);
    }

    /* Degenerate tables must not crash. */
    CHECK(PcLutSample(NULL, 256, scale, 100) == PC_GAIN_UNITY_Q16, "null lut");
    CHECK(PcLutSample(lut, 0, scale, 100) == PC_GAIN_UNITY_Q16, "empty lut");
    CHECK(PcLutSample(lut, 1, 0, 100) == lut[0], "single knot lut");
}

static void test_curves(void)
{
    int m;

    for (m = 0; m < PC_CURVE_MODE_COUNT; m++) {
        PC_CURVE_PARAMS p = PcPresets[m].Params;
        double prev = -1.0;
        int i;

        p.Mode = m;
        for (i = 0; i <= 200; i++) {
            double speed = (double)i;
            double g = PcEvalGain(&p, speed);

            CHECK(isfinite(g), "mode %d: non-finite gain at %f", m, speed);
            CHECK(g >= PC_GAIN_MIN_Q16 / 65536.0, "mode %d: gain below floor", m);
            CHECK(g <= PC_CAP_MAX, "mode %d: gain above ceiling", m);
            if (m != PC_CURVE_JUMP) {
                CHECK(g >= prev - 1e-9,
                      "mode %d: gain decreased at %f (%f -> %f)", m, speed, prev, g);
            }
            prev = g;
        }
    }

    /* Cap must actually bind. */
    {
        PC_CURVE_PARAMS p = PcPresets[1].Params;

        p.Cap = 1.25;
        CHECK(PcEvalGain(&p, 200.0) <= 1.25 + 1e-9,
              "cap ignored: %f", PcEvalGain(&p, 200.0));
    }

    /* Offset must produce a dead zone. */
    {
        PC_CURVE_PARAMS p = PcPresets[1].Params;

        p.Offset = 10.0;
        CHECK(fabs(PcEvalGain(&p, 5.0) - p.Base) < 1e-12, "dead zone broken");
    }

    /* Malformed input (NaN, absurd values) must be clamped, not propagated. */
    {
        PC_CURVE_PARAMS p = PcPresets[2].Params;

        p.Base = -5.0;
        p.Accel = 1e9;
        p.MaxSpeed = 0.0;
        CHECK(isfinite(PcEvalGain(&p, 50.0)), "malformed params produced NaN");
    }
}

static void test_snap(void)
{
    pc_i32 ax[4] = { 65536, 0, -65536, 0 };
    pc_i32 ay[4] = { 0, 65536, 0, -65536 };
    pc_i64 x, y;
    int applied;

    /* A vector 12 degrees off the X axis, 8 degrees of tolerance -> no snap. */
    x = 1000000;
    y = (pc_i64)(1000000.0 * tan(12.0 * 3.14159265358979 / 180.0));
    applied = PcSnapVectorQ16(&x, &y, ax, ay, 4,
                              (pc_i32)(cos(8.0 * 3.14159265358979 / 180.0) * 65536.0),
                              PC_Q16_ONE);
    CHECK(applied == 0, "snapped a vector outside the tolerance");

    /* Same vector with 20 degrees of tolerance and full strength -> exactly X. */
    x = 1000000;
    y = (pc_i64)(1000000.0 * tan(12.0 * 3.14159265358979 / 180.0));
    applied = PcSnapVectorQ16(&x, &y, ax, ay, 4,
                              (pc_i32)(cos(20.0 * 3.14159265358979 / 180.0) * 65536.0),
                              PC_Q16_ONE);
    CHECK(applied == 1, "did not snap a vector inside the tolerance");
    CHECK(y == 0, "full snap left y = %lld", (long long)y);
    CHECK(llabs((long long)x - 1000000) < 20000, "snap changed the magnitude: %lld",
          (long long)x);

    /* Half strength must land in between. */
    x = 1000000;
    y = 200000;
    applied = PcSnapVectorQ16(&x, &y, ax, ay, 4, 0, PC_Q16_HALF);
    CHECK(applied == 1, "zero tolerance should still snap to the nearest axis");
    CHECK(y > 0 && y < 200000, "half strength did not blend: %lld", (long long)y);

    /* Zero vector must be a no-op. */
    x = 0;
    y = 0;
    CHECK(PcSnapVectorQ16(&x, &y, ax, ay, 4, 0, PC_Q16_ONE) == 0, "snapped a null vector");
}

static void test_presets(void)
{
    int i;

    for (i = 0; i < PC_PRESET_COUNT; i++) {
        PC_CURVE_PARAMS p = PcPresets[i].Params;

        CHECK(p.Base >= PC_BASE_MIN && p.Base <= PC_BASE_MAX, "preset %d base", i);
        CHECK(p.Cap >= p.Base, "preset %d cap below base", i);
        CHECK(p.MaxSpeed >= PC_MAXSPEED_MIN && p.MaxSpeed <= PC_MAXSPEED_MAX,
              "preset %d max speed", i);
        CHECK(p.Mode >= 0 && p.Mode < PC_CURVE_MODE_COUNT, "preset %d mode", i);
    }
}

int main(void)
{
    test_isqrt();
    test_vec_len();
    test_axis_gain();
    test_lut();
    test_curves();
    test_snap();
    test_presets();

    if (g_failures == 0) {
        printf("OK: all shared math tests passed\n");
        return 0;
    }
    printf("%d test(s) FAILED\n", g_failures);
    return 1;
}
