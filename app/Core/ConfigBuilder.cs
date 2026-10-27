using System;
using System.Runtime.InteropServices;

namespace PrecisionCurve.Core
{
    /// <summary>
    /// Translates a <see cref="Profile"/> into the exact buffer the driver
    /// expects. This is the only place where UI concepts (stages, snap lines,
    /// degrees) become Q16.16 kernel values.
    /// </summary>
    public static class ConfigBuilder
    {
        /// <summary>Axes emitted per snap "line" pair.</summary>
        public const int MaxSnapAxes = NativeMethods.SnapAxisMax;

        public static NativeMethods.PC_SET_CONFIG Build(Profile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            profile.Curve.Clamp();

            var cfg = new NativeMethods.PC_SET_CONFIG
            {
                StructSize = (uint)Marshal.SizeOf<NativeMethods.PC_SET_CONFIG>(),
                AbiVersion = NativeMethods.AbiVersion,
                CurveMode = (uint)profile.Curve.Mode,
                ActiveStage = (uint)Math.Clamp(profile.ActiveStage, 0, NativeMethods.StageMax - 1),
                StageScaleXQ16 = new int[NativeMethods.StageMax],
                StageScaleYQ16 = new int[NativeMethods.StageMax],
                SnapAxis = new NativeMethods.PC_SNAP_AXIS[NativeMethods.SnapAxisMax],
                Lut = new int[NativeMethods.LutMax]
            };

            // ---- flags -----------------------------------------------------
            uint flags = 0;
            if (profile.Enabled) flags |= NativeMethods.FlagEnabled;
            if (profile.Snap.Enabled) flags |= NativeMethods.FlagSnapEnabled;
            if (profile.FramePacing) flags |= NativeMethods.FlagDtSmoothing;
            if (profile.InvertX) flags |= NativeMethods.FlagInvertX;
            if (profile.InvertY) flags |= NativeMethods.FlagInvertY;
            cfg.Flags = flags;

            // ---- gain table ------------------------------------------------
            int[] lut = CurveMath.BuildLut(profile.Curve, NativeMethods.LutMax, out uint maxSpeedQ16);
            Array.Copy(lut, cfg.Lut, NativeMethods.LutMax);
            cfg.LutCount = (uint)NativeMethods.LutMax;
            cfg.LutMaxSpeedQ16 = maxSpeedQ16;

            cfg.GainMinQ16 = NativeMethods.GainMinQ16;
            cfg.GainMaxQ16 = (int)Math.Round(Math.Min(profile.Curve.Cap, CurveMath.CapMax) * 65536.0);
            cfg.GainMaxQ16 = Math.Clamp(cfg.GainMaxQ16, cfg.GainMinQ16 + 1, NativeMethods.GainMaxQ16);

            cfg.MaxDeltaPerPacket = NativeMethods.OutputAbsMax;

            // ---- DPI stages (pure software scaling) ------------------------
            for (int i = 0; i < NativeMethods.StageMax; i++)
            {
                StageDefinition stage = (profile.Stages != null && i < profile.Stages.Count)
                    ? profile.Stages[i]
                    : new StageDefinition { ScaleX = 1.0, ScaleY = 1.0 };

                cfg.StageScaleXQ16[i] = ScaleToQ16(stage.ScaleX);
                cfg.StageScaleYQ16[i] = ScaleToQ16(stage.ScaleY);
            }

            // ---- snapping --------------------------------------------------
            if (profile.Snap.Enabled)
            {
                BuildSnapAxes(profile.Snap, cfg);
            }
            else
            {
                cfg.SnapAxisCount = 0;
                cfg.SnapMinCosQ16 = NativeMethods.Q16One;
                cfg.SnapStrengthQ16 = NativeMethods.Q16One;
            }

            return cfg;
        }

        private static int ScaleToQ16(double scale)
        {
            double q = scale * 65536.0;
            return (int)Math.Clamp(Math.Round(q), NativeMethods.GainMinQ16, NativeMethods.GainMaxQ16);
        }

        /// <summary>
        /// Snap axes are unit vectors. 2 lines = the 4 cardinal directions,
        /// 4 lines = cardinals + diagonals (the 8-axis ABI limit).
        /// </summary>
        private static void BuildSnapAxes(SnapSettings snap, NativeMethods.PC_SET_CONFIG cfg)
        {
            int lines = Math.Clamp(snap.LineCount, 1, 4);
            int axisCount = Math.Min(lines * 2, MaxSnapAxes);
            double step = 180.0 / lines;

            cfg.SnapAxisCount = (uint)axisCount;

            for (int i = 0; i < axisCount; i++)
            {
                double radians = i * step * Math.PI / 180.0;
                int x = (int)Math.Round(Math.Cos(radians) * 65536.0);
                int y = (int)Math.Round(Math.Sin(radians) * 65536.0);

                cfg.SnapAxis[i] = new NativeMethods.PC_SNAP_AXIS { X = x, Y = y };
            }

            double tolerance = Math.Clamp(snap.ToleranceDegrees, 0.0, 45.0);
            cfg.SnapMinCosQ16 = (int)Math.Round(Math.Cos(tolerance * Math.PI / 180.0) * 65536.0);
            cfg.SnapMinCosQ16 = Math.Clamp(cfg.SnapMinCosQ16, 0, NativeMethods.Q16One);

            cfg.SnapStrengthQ16 = (int)Math.Round(Math.Clamp(snap.Strength, 0.0, 1.0) * 65536.0);
        }

        /// <summary>
        /// Human-readable summary of the last build, shown in the status bar so
        /// the user can see exactly what the kernel is running.
        /// </summary>
        public static string Describe(Profile profile, NativeMethods.PC_SET_CONFIG cfg)
        {
            double baseGain = CurveMath.Evaluate(profile.Curve, 0.0);
            double fastGain = CurveMath.Evaluate(profile.Curve, profile.Curve.MaxSpeed);
            return $"LUT {cfg.LutCount} knots @ {cfg.LutMaxSpeedQ16 / 65536.0:0.#} c/ms | " +
                   $"gain {baseGain:0.00}x -> {fastGain:0.00}x | " +
                   $"stages {cfg.StageScaleXQ16[(int)cfg.ActiveStage] / 65536.0:0.00}x | " +
                   $"snap {(cfg.SnapAxisCount > 0 ? cfg.SnapAxisCount + " axes" : "off")}";
        }
    }
}
