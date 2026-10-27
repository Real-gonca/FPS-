using System;
using System.Collections.Generic;
using System.Linq;

namespace PrecisionCurve.Core
{
    /// <summary>One DPI stage: a pure software scale applied to the raw deltas.</summary>
    public sealed class StageDefinition
    {
        public string Name { get; set; } = "Stage";
        public double ScaleX { get; set; } = 1.0;
        public double ScaleY { get; set; } = 1.0;

        public StageDefinition Clone() => new StageDefinition
        {
            Name = Name,
            ScaleX = ScaleX,
            ScaleY = ScaleY
        };
    }

    /// <summary>
    /// Axis snapping: pulls movement towards the nearest of N directions.
    /// Max 8 axes because that is what the kernel ABI carries; 2 lines gives
    /// the 4 cardinal axes, 4 lines adds the diagonals.
    /// </summary>
    public sealed class SnapSettings
    {
        public bool Enabled { get; set; }
        public int LineCount { get; set; } = 2;          // 2 or 4
        public double ToleranceDegrees { get; set; } = 8.0;
        public double Strength { get; set; } = 1.0;      // 0..1

        public SnapSettings Clone() => new SnapSettings
        {
            Enabled = Enabled,
            LineCount = LineCount,
            ToleranceDegrees = ToleranceDegrees,
            Strength = Strength
        };
    }

    /// <summary>
    /// A complete, self-contained configuration. Switching profiles pushes the
    /// whole thing to the driver in a single IOCTL, so there is never a moment
    /// where the kernel has a half-applied profile.
    /// </summary>
    public sealed class Profile
    {
        public string Name { get; set; } = "New profile";
        public bool Enabled { get; set; } = true;

        public CurveParameters Curve { get; set; } = new CurveParameters();
        public List<StageDefinition> Stages { get; set; } = new List<StageDefinition>();
        public int ActiveStage { get; set; }

        /// <summary>
        /// The mouse's real DPI. Only used to *display* the effective DPI of a
        /// stage; software cannot change the sensor's DPI.
        /// </summary>
        public int HardwareDpi { get; set; } = 800;

        public SnapSettings Snap { get; set; } = new SnapSettings();

        public bool InvertX { get; set; }
        public bool InvertY { get; set; }

        /// <summary>Stabilise the report interval estimate (software frame pacing).</summary>
        public bool FramePacing { get; set; } = true;

        /// <summary>Process names (without .exe) that auto-select this profile.</summary>
        public List<string> Processes { get; set; } = new List<string>();

        public Profile Clone() => new Profile
        {
            Name = Name,
            Enabled = Enabled,
            Curve = Curve.Clone(),
            Stages = Stages.Select(s => s.Clone()).ToList(),
            ActiveStage = ActiveStage,
            HardwareDpi = HardwareDpi,
            Snap = Snap.Clone(),
            InvertX = InvertX,
            InvertY = InvertY,
            FramePacing = FramePacing,
            Processes = new List<string>(Processes)
        };

        public double EffectiveDpi
        {
            get
            {
                if (Stages.Count == 0) return HardwareDpi;
                int index = Math.Clamp(ActiveStage, 0, Stages.Count - 1);
                return HardwareDpi * Stages[index].ScaleX;
            }
        }

        public string EffectiveDpiText => $"{Math.Round(EffectiveDpi)} DPI (software)";

        // ------------------------------------------------------------------
        // Factory presets
        // ------------------------------------------------------------------

        public static List<StageDefinition> DefaultStages() => new List<StageDefinition>
        {
            new StageDefinition { Name = "Low",    ScaleX = 0.6, ScaleY = 0.6 },
            new StageDefinition { Name = "Normal", ScaleX = 1.0, ScaleY = 1.0 },
            new StageDefinition { Name = "High",   ScaleX = 1.5, ScaleY = 1.5 }
        };

        public static Profile CreateDefault(string name = "Desktop")
        {
            var p = new Profile
            {
                Name = name,
                Stages = DefaultStages(),
                ActiveStage = 1,
                Curve = new CurveParameters
                {
                    Mode = CurveMode.Linear,
                    Base = 1.0,
                    Offset = 0.0,
                    Accel = 0.0,
                    Exponent = 1.0,
                    Cap = 1.0,
                    MaxSpeed = 100.0
                }
            };
            return p;
        }

        /// <summary>
        /// Ready-made profiles. These are PrecisionCurve's own parameterisations
        /// -- inspired by, but deliberately not bit-identical to, Raw Accel.
        /// </summary>
        public static Profile FromPreset(string preset, string profileName = null)
        {
            Profile p = CreateDefault(profileName ?? preset);

            switch (preset)
            {
                case "FPS Flick":
                    p.Curve = new CurveParameters
                    {
                        Mode = CurveMode.Classic,
                        Base = 0.85,
                        Offset = 1.5,
                        Accel = 0.060,
                        Exponent = 1.0,
                        Cap = 2.2,
                        MaxSpeed = 120.0
                    };
                    p.Stages = new List<StageDefinition>
                    {
                        new StageDefinition { Name = "Sniper", ScaleX = 0.55, ScaleY = 0.55 },
                        new StageDefinition { Name = "Normal", ScaleX = 1.00, ScaleY = 1.00 },
                        new StageDefinition { Name = "Flick",  ScaleX = 1.35, ScaleY = 1.35 }
                    };
                    p.ActiveStage = 1;
                    p.Processes = new List<string> { "cs2", "valorant", "r5apex", "fortnite" };
                    break;

                case "Tracking":
                    p.Curve = new CurveParameters
                    {
                        Mode = CurveMode.Natural,
                        Base = 0.90,
                        Offset = 0.5,
                        Accel = 0.015,
                        Exponent = 1.0,
                        Cap = 1.8,
                        MaxSpeed = 120.0
                    };
                    p.Processes = new List<string> { "overwatch", "quakechampions" };
                    break;

                case "Precision":
                    p.Curve = new CurveParameters
                    {
                        Mode = CurveMode.Motivity,
                        Base = 0.70,
                        Offset = 1.0,
                        Accel = 0.45,
                        Exponent = 1.6,
                        Cap = 2.0,
                        MaxSpeed = 80.0
                    };
                    break;

                case "Sniper":
                    p.Curve = new CurveParameters
                    {
                        Mode = CurveMode.Jump,
                        Base = 0.55,
                        Offset = 6.0,
                        Accel = 0.25,
                        Exponent = 1.0,
                        Cap = 1.6,
                        MaxSpeed = 60.0
                    };
                    p.Snap = new SnapSettings { Enabled = true, LineCount = 2, ToleranceDegrees = 4.0, Strength = 0.6 };
                    break;

                case "Unity":
                default:
                    break;
            }

            p.Curve.Clamp();
            return p;
        }

        public static readonly string[] PresetNames =
        {
            "Unity", "FPS Flick", "Tracking", "Precision", "Sniper"
        };
    }
}
