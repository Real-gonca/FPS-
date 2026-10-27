using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrecisionCurve.Core
{
    public enum CurveMode
    {
        Classic = 0,
        Linear = 1,
        Natural = 2,
        Motivity = 3,
        Jump = 4
    }

    /// <summary>
    /// Curve parameters. The names match shared/precisioncurve_curves.h.
    /// </summary>
    public sealed class CurveParameters
    {
        public CurveMode Mode { get; set; } = CurveMode.Linear;
        public double Base { get; set; } = 1.0;
        public double Offset { get; set; } = 0.0;
        public double Accel { get; set; } = 0.0;
        public double Exponent { get; set; } = 1.0;
        public double Cap { get; set; } = 1.0;
        public double MaxSpeed { get; set; } = 100.0;

        public CurveParameters Clone() => new CurveParameters
        {
            Mode = Mode,
            Base = Base,
            Offset = Offset,
            Accel = Accel,
            Exponent = Exponent,
            Cap = Cap,
            MaxSpeed = MaxSpeed
        };

        /// <summary>Clamps every field into the range the driver accepts.</summary>
        public void Clamp()
        {
            Base = CurveMath.Clamp(Base, CurveMath.BaseMin, CurveMath.BaseMax);
            Offset = CurveMath.Clamp(Offset, 0.0, CurveMath.OffsetMax);
            Accel = CurveMath.Clamp(Accel, 0.0, CurveMath.AccelMax);
            Exponent = CurveMath.Clamp(Exponent, CurveMath.ExponentMin, CurveMath.ExponentMax);
            Cap = CurveMath.Clamp(Cap, Base, CurveMath.CapMax);
            MaxSpeed = CurveMath.Clamp(MaxSpeed, CurveMath.MaxSpeedMin, CurveMath.MaxSpeedMax);
        }
    }

    /// <summary>
    /// C# mirror of shared/precisioncurve_curves.h.
    ///
    /// This is the reference implementation used by the UI and by the driver's
    /// LUT compiler input. It is verified against the C version by
    /// <see cref="SelfTest"/> using tests/parity/vectors.json.
    /// </summary>
    public static class CurveMath
    {
        public const double BaseMin = 0.05;
        public const double BaseMax = 10.0;
        public const double CapMax = 64.0;
        public const double MaxSpeedMin = 0.5;
        public const double MaxSpeedMax = 200.0;
        public const double AccelMax = 5.0;
        public const double OffsetMax = 100.0;
        public const double ExponentMin = 0.25;
        public const double ExponentMax = 6.0;

        private const double GainFloor = 656.0 / 65536.0;

        public static double Clamp(double v, double lo, double hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        /// <summary>Gain multiplier for an input speed in counts/millisecond.</summary>
        public static double Evaluate(CurveParameters p, double speed)
        {
            double baseGain = Clamp(p.Base, BaseMin, BaseMax);
            double offset = Clamp(p.Offset, 0.0, OffsetMax);
            double accel = Clamp(p.Accel, 0.0, AccelMax);
            double expo = Clamp(p.Exponent, ExponentMin, ExponentMax);
            double cap = Clamp(p.Cap, BaseMin, CapMax);
            double maxSpd = Clamp(p.MaxSpeed, MaxSpeedMin, MaxSpeedMax);
            double s = Clamp(speed, 0.0, maxSpd);

            double x = s < offset ? 0.0 : s - offset;
            double g;

            switch (p.Mode)
            {
                case CurveMode.Linear:
                    g = baseGain + accel * x;
                    break;
                case CurveMode.Natural:
                    g = baseGain * Math.Exp(accel * x);
                    break;
                case CurveMode.Motivity:
                    g = baseGain + (accel * Math.Pow(x, expo)) / (x + 1.0);
                    break;
                case CurveMode.Jump:
                    g = x <= 0.0 ? baseGain : baseGain * (1.0 + accel) + accel * x;
                    break;
                default:
                    g = baseGain * (1.0 + accel * x);
                    break;
            }

            if (g > cap) g = cap;
            if (g < GainFloor) g = GainFloor;
            if (g > 64.0) g = 64.0;
            return g;
        }

        /// <summary>Same rounding as PcGainToQ16 in C (away from zero).</summary>
        public static int ToQ16(double gain)
        {
            double scaled = gain * 65536.0;
            return scaled >= 0.0 ? (int)(scaled + 0.5) : (int)(scaled - 0.5);
        }

        /// <summary>
        /// Compiles the curve into the integer table the driver samples.
        /// Knots are uniformly spaced over [0, MaxSpeed].
        /// </summary>
        public static int[] BuildLut(CurveParameters p, int count, out uint maxSpeedQ16)
        {
            if (count < 2) count = 2;
            if (count > NativeMethods.LutMax) count = NativeMethods.LutMax;

            double maxSpd = Clamp(p.MaxSpeed, MaxSpeedMin, MaxSpeedMax);
            uint maxQ16 = (uint)(maxSpd * 65536.0 + 0.5);
            if (maxQ16 > (uint)NativeMethods.SpeedMaxQ16) maxQ16 = (uint)NativeMethods.SpeedMaxQ16;

            int[] lut = new int[count];
            for (int i = 0; i < count; i++)
            {
                double speed = (maxSpd * i) / (count - 1);
                lut[i] = ToQ16(Evaluate(p, speed));
            }

            // Monotonic, in-range table: the driver holds the last knot beyond
            // MaxSpeed, so a non-monotonic table would produce a gain cliff.
            for (int i = 1; i < count; i++)
            {
                if (lut[i] < lut[i - 1]) lut[i] = lut[i - 1];
                if (lut[i] > NativeMethods.GainMaxQ16) lut[i] = NativeMethods.GainMaxQ16;
                if (lut[i] < NativeMethods.GainMinQ16) lut[i] = NativeMethods.GainMinQ16;
            }

            maxSpeedQ16 = maxQ16;
            return lut;
        }

        /// <summary>Points for the graph, one per pixel-ish sample.</summary>
        public static IReadOnlyList<(double Speed, double Gain)> SampleCurve(CurveParameters p, int samples)
        {
            if (samples < 2) samples = 2;
            var points = new List<(double, double)>(samples);
            double maxSpd = Clamp(p.MaxSpeed, MaxSpeedMin, MaxSpeedMax);

            for (int i = 0; i < samples; i++)
            {
                double speed = maxSpd * i / (samples - 1);
                points.Add((speed, Evaluate(p, speed)));
            }
            return points;
        }

        // ------------------------------------------------------------------
        // Cross-language verification
        // ------------------------------------------------------------------

        public sealed class VectorFile
        {
            public List<CurveVector> curve_vectors { get; set; }
            public List<LutVector> lut_vectors { get; set; }
        }

        public sealed class CurveVector
        {
            public int mode { get; set; }
            [JsonPropertyName("base")] public double BaseGain { get; set; }
            public double offset { get; set; }
            public double accel { get; set; }
            public double exponent { get; set; }
            public double cap { get; set; }
            public double max_speed { get; set; }
            public double speed { get; set; }
            public double gain { get; set; }
        }

        public sealed class LutVector
        {
            public string name { get; set; }
            public int mode { get; set; }
            [JsonPropertyName("base")] public double BaseGain { get; set; }
            public double offset { get; set; }
            public double accel { get; set; }
            public double exponent { get; set; }
            public double cap { get; set; }
            public double max_speed { get; set; }
            public int count { get; set; }
            public uint max_speed_q16 { get; set; }
            public int[] lut { get; set; }
        }

        /// <summary>
        /// Compares this implementation against the C-generated golden vectors.
        /// Run with:  PrecisionCurve.exe --selftest path\to\vectors.json
        /// </summary>
        public static int SelfTest(string vectorsPath, out string message)
        {
            if (!File.Exists(vectorsPath))
            {
                message = $"vectors not found: {vectorsPath}";
                return -1;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            VectorFile data = JsonSerializer.Deserialize<VectorFile>(File.ReadAllText(vectorsPath), options);

            int checks = 0;
            int failures = 0;
            double worst = 0.0;

            foreach (CurveVector v in data.curve_vectors)
            {
                var p = new CurveParameters
                {
                    Mode = (CurveMode)v.mode,
                    Base = v.BaseGain,
                    Offset = v.offset,
                    Accel = v.accel,
                    Exponent = v.exponent,
                    Cap = v.cap,
                    MaxSpeed = v.max_speed
                };

                double got = Evaluate(p, v.speed);
                double err = Math.Abs(got - v.gain);
                worst = Math.Max(worst, err);
                checks++;
                if (err > 1e-12) failures++;
            }

            foreach (LutVector v in data.lut_vectors)
            {
                var p = new CurveParameters
                {
                    Mode = (CurveMode)v.mode,
                    Base = v.BaseGain,
                    Offset = v.offset,
                    Accel = v.accel,
                    Exponent = v.exponent,
                    Cap = v.cap,
                    MaxSpeed = v.max_speed
                };

                int[] lut = BuildLut(p, v.count, out uint maxQ16);
                checks++;
                if (maxQ16 != v.max_speed_q16) failures++;

                for (int i = 0; i < v.count; i++)
                {
                    checks++;
                    if (lut[i] != v.lut[i]) failures++;
                }
            }

            message = failures == 0
                ? $"OK: {checks} curve checks match the C reference (max error {worst:E2})"
                : $"{failures} of {checks} curve checks FAILED";
            return failures;
        }
    }
}
