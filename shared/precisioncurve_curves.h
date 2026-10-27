/*++
    PrecisionCurve - curve definitions and LUT compiler

    Module:
        precisioncurve_curves.h

    Abstract:
        Floating-point evaluation of the acceleration curves and the compiler
        that turns a curve into the integer gain table consumed by the driver.

        USER MODE ONLY. Never include this from kernel code: the driver must
        not execute floating point, and this header needs libm.

        The GUI (C#) and the CLI (C++) re-implement exactly these formulas and
        are cross-checked against this header by the parity test in tests/.
        If you change a formula here, change it in:
            app/Core/CurveMath.cs
            tools/pcctl/main.cpp
            tests/parity/parity.js
        and run `make -C tests` before shipping.

    Environment:
        User mode only, C99.
--*/

#ifndef PRECISIONCURVE_CURVES_H
#define PRECISIONCURVE_CURVES_H

#if defined(_KERNEL_MODE)
#error "precisioncurve_curves.h is user-mode only (floating point + libm)"
#endif

#include <math.h>
#include "precisioncurve_math.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ------------------------------------------------------------------------- */
/* Curve family                                                              */
/* ------------------------------------------------------------------------- */

typedef enum _PC_CURVE_MODE {
    PC_CURVE_CLASSIC  = 0, /* g = B * (1 + A*x)                 multiplicative */
    PC_CURVE_LINEAR   = 1, /* g = B + A*x                       additive       */
    PC_CURVE_NATURAL  = 2, /* g = B * exp(A*x)                  exponential    */
    PC_CURVE_MOTIVITY = 3, /* g = B + A*x^E / (x + 1)           saturating     */
    PC_CURVE_JUMP     = 4  /* g = B*(1 + A*[s>=T]) + A*max(0,s-T) step       */
} PC_CURVE_MODE;

#define PC_CURVE_MODE_COUNT 5

/* ------------------------------------------------------------------------- */
/* Parameters                                                                */
/* ------------------------------------------------------------------------- */

typedef struct _PC_CURVE_PARAMS {
    int    Mode;      /* PC_CURVE_MODE                                  */
    double Base;      /* sensitivity floor, multiplier (0.05 .. 10.0)   */
    double Offset;    /* dead zone in counts/ms, subtracted before use  */
    double Accel;     /* curve strength                                 */
    double Exponent;  /* Motivity shaping exponent (ignored elsewhere)  */
    double Cap;       /* hard ceiling on the multiplier (cap > Base)    */
    double MaxSpeed;  /* table range in counts/ms (0.5 .. 200)          */
} PC_CURVE_PARAMS;

#define PC_BASE_MIN      0.05
#define PC_BASE_MAX      10.0
#define PC_CAP_MAX       64.0
#define PC_MAXSPEED_MIN  0.5
#define PC_MAXSPEED_MAX  200.0
#define PC_ACCEL_MAX     5.0
#define PC_OFFSET_MAX    100.0
#define PC_EXPONENT_MIN  0.25
#define PC_EXPONENT_MAX  6.0

/* ------------------------------------------------------------------------- */
/* Evaluation                                                                */
/* ------------------------------------------------------------------------- */

static double PcClampD(double v, double lo, double hi)
{
    if (v < lo) return lo;
    if (v > hi) return hi;
    return v;
}

/* Gain (multiplier) for a given input speed in counts/ms.
 * Identical to CurveMath.Evaluate() in the C# client. */
static double PcEvalGain(const PC_CURVE_PARAMS *p, double speed)
{
    double base   = PcClampD(p->Base, PC_BASE_MIN, PC_BASE_MAX);
    double offset = PcClampD(p->Offset, 0.0, PC_OFFSET_MAX);
    double accel  = PcClampD(p->Accel, 0.0, PC_ACCEL_MAX);
    double expo   = PcClampD(p->Exponent, PC_EXPONENT_MIN, PC_EXPONENT_MAX);
    double cap    = PcClampD(p->Cap, PC_BASE_MIN, PC_CAP_MAX);
    double maxSpd = PcClampD(p->MaxSpeed, PC_MAXSPEED_MIN, PC_MAXSPEED_MAX);
    double s      = PcClampD(speed, 0.0, maxSpd);
    double x;
    double g;

    if (s < offset) {
        x = 0.0;
    } else {
        x = s - offset;
    }

    switch (p->Mode) {
    case PC_CURVE_LINEAR:
        g = base + accel * x;
        break;

    case PC_CURVE_NATURAL:
        g = base * exp(accel * x);
        break;

    case PC_CURVE_MOTIVITY:
        g = base + (accel * pow(x, expo)) / (x + 1.0);
        break;

    case PC_CURVE_JUMP:
        /* Discontinuous by design: below the threshold only the base gain
         * applies, above it the gain jumps by (base * accel) and then grows
         * with slope accel. Offset is the trigger speed. */
        if (x <= 0.0) {
            g = base;
        } else {
            g = base * (1.0 + accel) + accel * x;
        }
        break;

    case PC_CURVE_CLASSIC:
    default:
        g = base * (1.0 + accel * x);
        break;
    }

    if (g > cap) {
        g = cap;
    }
    if (g < PC_GAIN_MIN_Q16 / 65536.0) {
        g = PC_GAIN_MIN_Q16 / 65536.0;
    }
    if (g > 64.0) {
        g = 64.0;
    }
    return g;
}

/* ------------------------------------------------------------------------- */
/* LUT compiler                                                              */
/* ------------------------------------------------------------------------- */

/* Converts a double gain to the Q16.16 integer the driver understands.
 * Rounding is away-from-zero so C, C++ and C# agree on every knot. */
static pc_i32 PcGainToQ16(double gain)
{
    double scaled = gain * 65536.0;

    if (scaled >= 0.0) {
        return (pc_i32)(scaled + 0.5);
    }
    return (pc_i32)(scaled - 0.5);
}

/* Fills lut[0..count-1] with uniformly spaced knots over [0, MaxSpeed].
 * Returns the maxSpeed actually used, in Q16.16 counts/ms. */
static pc_i32 PcBuildLut(const PC_CURVE_PARAMS *p,
                         pc_i32                *lut,
                         pc_u32                 count,
                         pc_u32                *maxSpeedQ16Out)
{
    pc_u32 i;
    double maxSpd = PcClampD(p->MaxSpeed, PC_MAXSPEED_MIN, PC_MAXSPEED_MAX);
    pc_u32 maxQ16;

    if (lut == NULL || count < 2 || maxSpeedQ16Out == NULL) {
        return 0;
    }

    maxQ16 = (pc_u32)(maxSpd * 65536.0 + 0.5);
    if (maxQ16 > (pc_u32)PC_SPEED_MAX_Q16) {
        maxQ16 = (pc_u32)PC_SPEED_MAX_Q16;
    }

    for (i = 0; i < count; i++) {
        double speed = (maxSpd * (double)i) / (double)(count - 1);
        double gain  = PcEvalGain(p, speed);

        lut[i] = PcGainToQ16(gain);
    }

    /* Force a monotonic non-decreasing table. The driver relies on this to
     * keep "beyond maxSpeed holds the last knot" sane, and it catches NaN or
     * a bad preset before it ever reaches the kernel. */
    for (i = 1; i < count; i++) {
        if (lut[i] < lut[i - 1]) {
            lut[i] = lut[i - 1];
        }
        if (lut[i] > PC_GAIN_MAX_Q16) {
            lut[i] = PC_GAIN_MAX_Q16;
        }
        if (lut[i] < PC_GAIN_MIN_Q16) {
            lut[i] = PC_GAIN_MIN_Q16;
        }
    }

    *maxSpeedQ16Out = maxQ16;
    return (pc_i32)maxQ16;
}

/* ------------------------------------------------------------------------- */
/* Presets (shared by the CLI; the GUI mirrors them in CurveMath.cs)          */
/* ------------------------------------------------------------------------- */

typedef struct _PC_PRESET {
    const char     *Name;
    PC_CURVE_PARAMS Params;
} PC_PRESET;

#define PC_PRESET_COUNT 5

static const PC_PRESET PcPresets[PC_PRESET_COUNT] = {
    { "Unity",        { PC_CURVE_LINEAR,   1.00, 0.0, 0.000, 1.0,  1.0, 100.0 } },
    { "FPS Flick",    { PC_CURVE_CLASSIC,  0.85, 1.5, 0.060, 1.0,  2.2, 120.0 } },
    { "Natural Track",{ PC_CURVE_NATURAL,  0.90, 0.5, 0.015, 1.0,  1.8, 120.0 } },
    { "Precision",    { PC_CURVE_MOTIVITY, 0.70, 1.0, 0.450, 1.6,  2.0,  80.0 } },
    { "Sniper Jump",  { PC_CURVE_JUMP,     0.55, 6.0, 0.250, 1.0,  1.6,  60.0 } }
};

#ifdef __cplusplus
}
#endif

#endif /* PRECISIONCURVE_CURVES_H */
