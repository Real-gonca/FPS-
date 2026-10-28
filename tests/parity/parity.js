/*
    PrecisionCurve - JavaScript mirror of the curve math.

    This file exists to prove the C reference (shared/precisioncurve_curves.h)
    and the C# client (app/Core/CurveMath.cs) agree. It is a line-by-line port
    of the C code; if you change one, change all three.

    Run:  node tests/parity/parity.js
*/

'use strict';

const fs = require('fs');
const path = require('path');

const BASE_MIN = 0.05;
const BASE_MAX = 10.0;
const CAP_MAX = 64.0;
const MAXSPEED_MIN = 0.5;
const MAXSPEED_MAX = 200.0;
const ACCEL_MAX = 5.0;
const OFFSET_MAX = 100.0;
const EXPONENT_MIN = 0.25;
const EXPONENT_MAX = 6.0;

const PC_GAIN_MIN = 656 / 65536;

function clamp(v, lo, hi) {
    if (v < lo) return lo;
    if (v > hi) return hi;
    return v;
}

// mode: 0 Classic, 1 Linear, 2 Natural, 3 Motivity, 4 Jump
function evaluate(p, speed) {
    const base = clamp(p.base, BASE_MIN, BASE_MAX);
    const offset = clamp(p.offset, 0.0, OFFSET_MAX);
    const accel = clamp(p.accel, 0.0, ACCEL_MAX);
    const expo = clamp(p.exponent, EXPONENT_MIN, EXPONENT_MAX);
    const cap = clamp(p.cap, BASE_MIN, CAP_MAX);
    const maxSpd = clamp(p.max_speed, MAXSPEED_MIN, MAXSPEED_MAX);
    const s = clamp(speed, 0.0, maxSpd);
    const x = s < offset ? 0.0 : s - offset;

    let g;
    switch (p.mode) {
        case 1:
            g = base + accel * x;
            break;
        case 2:
            g = base * Math.exp(accel * x);
            break;
        case 3:
            g = base + (accel * Math.pow(x, expo)) / (x + 1.0);
            break;
        case 4:
            g = x <= 0.0 ? base : base * (1.0 + accel) + accel * x;
            break;
        default:
            g = base * (1.0 + accel * x);
            break;
    }

    if (g > cap) g = cap;
    if (g < PC_GAIN_MIN) g = PC_GAIN_MIN;
    if (g > 64.0) g = 64.0;
    return g;
}

function gainToQ16(gain) {
    const scaled = gain * 65536.0;
    return scaled >= 0 ? Math.floor(scaled + 0.5) : Math.floor(scaled - 0.5);
}

function buildLut(p, count) {
    const maxSpd = clamp(p.max_speed, MAXSPEED_MIN, MAXSPEED_MAX);
    const lut = new Array(count);

    for (let i = 0; i < count; i++) {
        const speed = (maxSpd * i) / (count - 1);
        lut[i] = gainToQ16(evaluate(p, speed));
    }
    for (let i = 1; i < count; i++) {
        if (lut[i] < lut[i - 1]) lut[i] = lut[i - 1];
        if (lut[i] > 64 * 65536) lut[i] = 64 * 65536;
        if (lut[i] < 656) lut[i] = 656;
    }
    const maxQ16 = Math.min(Math.floor(maxSpd * 65536.0 + 0.5), 200 * 65536);
    return { lut, maxQ16 };
}

// ---- driver-side sampling, mirrored exactly from precisioncurve_math.h ----
function lutScale(count, maxSpeedQ16) {
    if (count < 2 || maxSpeedQ16 === 0) return 0;
    const num = BigInt(count - 1) << 32n;
    const den = BigInt(maxSpeedQ16);
    return Number((num + den - 1n) / den);
}

function lutSample(lut, count, scale, speedQ16) {
    if (!lut || count === 0) return 65536;
    if (count === 1) return lut[0];
    if (speedQ16 <= 0) return lut[0];

    const posQ16 = Number((BigInt(speedQ16) * BigInt(scale)) >> 16n);
    const idx = Math.floor(posQ16 / 65536);
    if (idx >= count - 1) return lut[count - 1];

    const frac = posQ16 & 0xffff;
    const v0 = lut[idx];
    const v1 = lut[idx + 1];
    return v0 + Math.floor(((v1 - v0) * frac) / 65536);
}

function main() {
    const file = path.join(__dirname, 'vectors.json');
    const data = JSON.parse(fs.readFileSync(file, 'utf8'));
    let failures = 0;
    let checks = 0;
    let worst = 0;

    for (const v of data.curve_vectors) {
        const got = evaluate(v, v.speed);
        const err = Math.abs(got - v.gain);
        checks++;
        worst = Math.max(worst, err);
        if (err > 1e-12) {
            failures++;
            if (failures < 10) {
                console.error(`curve mismatch: mode=${v.mode} speed=${v.speed} ` +
                    `C=${v.gain} JS=${got}`);
            }
        }
    }

    for (const v of data.lut_vectors) {
        const built = buildLut(v, v.count);
        checks++;
        if (built.maxQ16 !== v.max_speed_q16) {
            failures++;
            console.error(`maxQ16 mismatch for ${v.name}: ${v.max_speed_q16} vs ${built.maxQ16}`);
        }
        if (built.lut.length !== v.lut.length) {
            failures++;
            console.error(`LUT length mismatch for ${v.name}`);
            continue;
        }
        let lutMismatch = 0;
        for (let i = 0; i < v.lut.length; i++) {
            checks++;
            if (built.lut[i] !== v.lut[i]) {
                lutMismatch++;
                failures++;
                if (lutMismatch <= 5) {
                    console.error(`LUT mismatch ${v.name}[${i}]: C=${v.lut[i]} JS=${built.lut[i]}`);
                }
            }
        }

        // Sampling the compiled table must reproduce the curve at the knots.
        const scale = lutScale(v.count, v.max_speed_q16);
        for (let i = 0; i < v.count; i++) {
            const speedQ16 = Math.floor((i * v.max_speed_q16) / (v.count - 1));
            const sampled = lutSample(v.lut, v.count, scale, speedQ16);
            const expected = v.lut[i];
            checks++;
            if (Math.abs(sampled - expected) > 1) {
                failures++;
                if (failures < 20) {
                    console.error(`sample mismatch ${v.name}[${i}]: ${sampled} vs ${expected}`);
                }
            }
        }
    }

    if (failures === 0) {
        console.log(`OK: ${checks} parity checks passed (max gain error ${worst.toExponential(2)})`);
        return 0;
    }
    console.error(`${failures} of ${checks} parity checks FAILED`);
    return 1;
}

process.exit(main());
