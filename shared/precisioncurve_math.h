/*++
    PrecisionCurve - shared fixed-point math

    Module:
        precisioncurve_math.h

    Abstract:
        Freestanding, allocation-free, libm-free helpers shared by the kernel
        driver and the user-mode client. Everything here is safe to run at
        DISPATCH_LEVEL: no floating point, no pageable memory, no exceptions,
        no unbounded loops.

        All "Q16" values are signed 16.16 fixed point: value = raw / 65536.

        The driver's hot path MUST only use the routines in this file. The
        floating-point curve evaluation lives in precisioncurve_curves.h and is
        user-mode only (it compiles the LUT that this file samples).

    Environment:
        Kernel mode and user mode, C (MSVC / clang-cl / gcc).
--*/

#ifndef PRECISIONCURVE_MATH_H
#define PRECISIONCURVE_MATH_H

#if defined(_MSC_VER)
typedef signed   __int32 pc_i32;
typedef unsigned __int32 pc_u32;
typedef signed   __int64 pc_i64;
typedef unsigned __int64 pc_u64;
#else
#include <stdint.h>
#include <stddef.h>
typedef int32_t  pc_i32;
typedef uint32_t pc_u32;
typedef int64_t  pc_i64;
typedef uint64_t pc_u64;
#endif

#if defined(_MSC_VER)
#  define PC_INLINE static __inline
#else
#  define PC_INLINE static inline
#endif

/* ------------------------------------------------------------------------- */
/* Constants                                                                 */
/* ------------------------------------------------------------------------- */

#define PC_Q16_ONE           ((pc_i32)65536)
#define PC_Q16_HALF          ((pc_i32)32768)
#define PC_Q16_FRAC_MASK     ((pc_u32)0xFFFF)

/* Gain is the multiplier applied to the raw delta: 65536 == 1.0x */
#define PC_GAIN_UNITY_Q16    PC_Q16_ONE
#define PC_GAIN_MAX_Q16      ((pc_i32)(64 * PC_Q16_ONE))    /* 64x hard ceiling  */
#define PC_GAIN_MIN_Q16      ((pc_i32)656)                  /* 0.01x             */

/* Speed is expressed in sensor counts per millisecond. 200 counts/ms equals
 * 200.000 counts/s, comfortably above any consumer mouse. */
#define PC_SPEED_MAX_Q16     ((pc_i32)(200 * PC_Q16_ONE))

/* Largest |dx| or |dy| we will accept from a packet before clamping. Relative
 * mice are physically limited to +/-32767 per report; anything above is either
 * a corrupted report or an absolute device we must not touch. */
#define PC_DELTA_ABS_MAX     ((pc_i32)32767)

/* Largest |output| per packet per axis. Protects against a bad LUT or a bad
 * timestamp turning into a cursor teleport across the screen. */
#define PC_OUTPUT_ABS_MAX    ((pc_i32)8192)

/* ------------------------------------------------------------------------- */
/* Scalar helpers                                                            */
/* ------------------------------------------------------------------------- */

PC_INLINE pc_i32 PcClampI32(pc_i32 v, pc_i32 lo, pc_i32 hi)
{
    if (v < lo) return lo;
    if (v > hi) return hi;
    return v;
}

PC_INLINE pc_i64 PcClampI64(pc_i64 v, pc_i64 lo, pc_i64 hi)
{
    if (v < lo) return lo;
    if (v > hi) return hi;
    return v;
}

PC_INLINE pc_i32 PcAbsI32(pc_i32 v)
{
    return (v < 0) ? (pc_i32)(-v) : v;
}

/* (a * b) >> 16, saturated to the pc_i32 range. Never overflows. */
PC_INLINE pc_i32 PcMulQ16(pc_i32 a, pc_i32 b)
{
    pc_i64 p = (pc_i64)a * (pc_i64)b;
    p >>= 16;
    if (p > (pc_i64)0x7FFFFFFF) return (pc_i32)0x7FFFFFFF;
    if (p < (pc_i64)(-0x7FFFFFFF - 1)) return (pc_i32)(-0x7FFFFFFF - 1);
    return (pc_i32)p;
}

/* a / b in 16.16, saturating, with b == 0 returning 0. */
PC_INLINE pc_i32 PcDivQ16(pc_i32 a, pc_i32 b)
{
    pc_i64 n;
    if (b == 0) {
        return 0;
    }
    n = ((pc_i64)a << 16) / (pc_i64)b;
    n = PcClampI64(n, (pc_i64)(-0x7FFFFFFF - 1), (pc_i64)0x7FFFFFFF);
    return (pc_i32)n;
}

/* ------------------------------------------------------------------------- */
/* Integer square root (digit-by-digit, 32 iterations, no division)          */
/* ------------------------------------------------------------------------- */

PC_INLINE pc_u32 PcISqrt64(pc_u64 value)
{
    pc_u64 rem  = 0;
    pc_u64 root = 0;
    pc_u32 i;

    for (i = 0; i < 32; i++) {
        pc_u64 trial;

        rem = (rem << 2) | ((value >> 62) & 3u);
        value <<= 2;

        trial = (root << 2) | 1u;
        root <<= 1;
        if (rem >= trial) {
            rem -= trial;
            root++;
        }
    }
    return (pc_u32)root;
}

/* ------------------------------------------------------------------------- */
/* Vector helpers (all inputs already in Q16.16)                             */
/* ------------------------------------------------------------------------- */

/* Length of a raw integer delta vector, expressed in Q16.16.
 * Inputs are clamped to +/-32767 so the square sum cannot overflow. */
PC_INLINE pc_i32 PcVecLenQ16FromRaw(pc_i32 dx, pc_i32 dy)
{
    pc_i64 lx = (pc_i64)PcClampI32(dx, -PC_DELTA_ABS_MAX, PC_DELTA_ABS_MAX);
    pc_i64 ly = (pc_i64)PcClampI32(dy, -PC_DELTA_ABS_MAX, PC_DELTA_ABS_MAX);
    pc_u64 sq = (pc_u64)(lx * lx + ly * ly);

    pc_u64 len;

    /* sqrt(sq) << 16  ==  sqrt(sq << 32). A 32767/32767 diagonal is
     * ~3.0e9 in Q16 and does not fit a signed 32-bit value, so saturate. */
    len = (pc_u64)PcISqrt64(sq << 32);
    if (len > 0x7FFFFFFFull) {
        return (pc_i32)0x7FFFFFFF;
    }
    return (pc_i32)len;
}

/* Length of a Q16.16 vector. Callers must keep components below ~2^31 so the
 * square sum stays inside pc_i64. */
PC_INLINE pc_i64 PcVecLenQ16FromQ16(pc_i64 xQ16, pc_i64 yQ16)
{
    pc_u64 sq = (pc_u64)(xQ16 * xQ16 + yQ16 * yQ16);

    /* sqrt(sq << 32) would overflow for large vectors: use sqrt(sq) directly,
     * the inputs are already Q16 so the result is Q16. */
    return (pc_i64)PcISqrt64(sq);
}

/* ------------------------------------------------------------------------- */
/* LUT sampling                                                              */
/* ------------------------------------------------------------------------- */

/* Pre-computed by the driver at publish time so the hot path stays cheap.
 *
 *   scale = ceil((count - 1) * 2^32 / maxSpeedQ16)
 *
 * and the sample position is  posQ16 = (speedQ16 * scale) >> 16, which yields
 * the Q16.16 table index. Derivation: at speed == maxSpeed the position must
 * be exactly (count - 1) << 16. */
PC_INLINE pc_u32 PcLutScaleQ32(pc_u32 count, pc_u32 maxSpeedQ16)
{
    if (count < 2 || maxSpeedQ16 == 0) {
        return 0;
    }
    /* Round up: with truncation, sampling exactly at maxSpeed lands just
     * below the last knot and reports a value short of lut[count-1]. */
    return (pc_u32)(((((pc_u64)(count - 1)) << 32) + (pc_u64)maxSpeedQ16 - 1) /
                    (pc_u64)maxSpeedQ16);
}

/* Piecewise-linear sample of the gain table.
 *
 * lut[0] is the gain at speed 0, lut[count-1] the gain at maxSpeed. Speed is
 * clamped to the table range, so beyond maxSpeed the last knot is held (which
 * is exactly what "Cap" means for a monotonic curve). */
PC_INLINE pc_i32 PcLutSample(const pc_i32 *lut,
                             pc_u32       count,
                             pc_u32       scaleQ32,
                             pc_i32       speedQ16)
{
    pc_u64 posQ16;
    pc_u32 idx;
    pc_u32 frac;
    pc_i32 v0;
    pc_i32 v1;

    if (lut == NULL || count == 0) {
        return PC_GAIN_UNITY_Q16;
    }
    if (count == 1) {
        return lut[0];
    }
    if (speedQ16 <= 0) {
        return lut[0];
    }

    posQ16 = ((pc_u64)(pc_u32)speedQ16 * (pc_u64)scaleQ32) >> 16; /* Q16 index */
    idx    = (pc_u32)(posQ16 >> 16);

    if (idx >= count - 1) {
        return lut[count - 1];
    }

    frac = (pc_u32)(posQ16 & PC_Q16_FRAC_MASK);
    v0   = lut[idx];
    v1   = lut[idx + 1];

    return v0 + (pc_i32)(((pc_i64)(v1 - v0) * (pc_i64)(pc_i32)frac) >> 16);
}

/* ------------------------------------------------------------------------- */
/* Per-axis application with sub-count error diffusion                       */
/* ------------------------------------------------------------------------- */

/* Multiplies one axis delta by gainQ16 and keeps the fractional remainder in
 * *residualQ16 so that slow movement is not lost to truncation.
 *
 * The caller owns *residualQ16 and must keep it within +/-65535; the routine
 * always writes a value inside that range. */
PC_INLINE pc_i32 PcApplyAxisGain(pc_i32  delta,
                                 pc_i32  gainQ16,
                                 pc_i32 *residualQ16)
{
    pc_i64 scaled;
    pc_i64 out;

    scaled = (pc_i64)delta * (pc_i64)gainQ16 + (pc_i64)(*residualQ16);

    if (scaled >= 0) {
        out = (scaled + (pc_i64)PC_Q16_HALF) >> 16;
    } else {
        out = -(((-scaled) + (pc_i64)PC_Q16_HALF) >> 16);
    }

    *residualQ16 = (pc_i32)(scaled - (out << 16));
    return (pc_i32)out;
}

/* ------------------------------------------------------------------------- */
/* Output vector clamping and axis snapping                                  */
/* ------------------------------------------------------------------------- */

PC_INLINE void PcClampVecQ16(pc_i64 *xQ16, pc_i64 *yQ16, pc_i32 limitCounts)
{
    pc_i64 lim = (pc_i64)limitCounts << 16;

    *xQ16 = PcClampI64(*xQ16, -lim, lim);
    *yQ16 = PcClampI64(*yQ16, -lim, lim);
}

/* Blends a movement vector towards the nearest of a set of unit axes.
 *
 * axisX/axisY are Q16.16 unit vectors supplied by user mode (validated by the
 * IOCTL handler). minCosQ16 is the smallest cosine accepted as "close enough"
 * to an axis; strengthQ16 is the 0..1 blend factor.
 *
 * Returns non-zero when a snap was applied. Both vectors must already be
 * clamped by PcClampVecQ16 so the squares cannot overflow.
 */
PC_INLINE int PcSnapVectorQ16(pc_i64 *xQ16,
                              pc_i64 *yQ16,
                              const pc_i32 *axisX,
                              const pc_i32 *axisY,
                              pc_u32        axisCount,
                              pc_i32        minCosQ16,
                              pc_i32        strengthQ16)
{
    pc_i64 bestDot  = 0;
    pc_i32 bestIdx  = -1;
    pc_i64 lenQ16;
    pc_u32 i;
    pc_i64 dot;
    pc_i64 targetX;
    pc_i64 targetY;

    lenQ16 = PcVecLenQ16FromQ16(*xQ16, *yQ16);
    if (lenQ16 <= 0 || axisCount == 0) {
        return 0;
    }

    for (i = 0; i < axisCount; i++) {
        /* cos(theta) = dot / (|v| * |axis|), with |axis| == 1.0 */
        dot = ((*xQ16 * (pc_i64)axisX[i]) + (*yQ16 * (pc_i64)axisY[i])) >> 16;
        if (dot < 0) {
            dot = -dot;
        }
        if (dot > bestDot) {
            bestDot = dot;
            bestIdx = (pc_i32)i;
        }
    }

    if (bestIdx < 0) {
        return 0;
    }

    /* bestDot/lenQ16 is the cosine; compare without dividing. */
    if (bestDot * (pc_i64)PC_Q16_ONE < (pc_i64)minCosQ16 * lenQ16) {
        return 0;
    }

    dot     = ((*xQ16 * (pc_i64)axisX[bestIdx]) +
               (*yQ16 * (pc_i64)axisY[bestIdx])) >> 16;
    targetX = (dot * (pc_i64)axisX[bestIdx]) >> 16;
    targetY = (dot * (pc_i64)axisY[bestIdx]) >> 16;

    *xQ16 += ((targetX - *xQ16) * (pc_i64)strengthQ16) >> 16;
    *yQ16 += ((targetY - *yQ16) * (pc_i64)strengthQ16) >> 16;
    return 1;
}

#endif /* PRECISIONCURVE_MATH_H */
