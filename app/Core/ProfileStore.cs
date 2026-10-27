using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrecisionCurve.Core
{
    internal sealed class SettingsFile
    {
        public int Version { get; set; } = 1;
        public string ActiveProfile { get; set; }
        public bool AutoSwitch { get; set; } = true;
        public List<Profile> Profiles { get; set; } = new List<Profile>();
    }

    /// <summary>
    /// Profile persistence: %APPDATA%\PrecisionCurve\settings.json.
    ///
    /// Everything is plain JSON so power users can version-control their setup.
    /// A corrupt file is backed up and replaced with defaults instead of
    /// preventing the application from starting.
    /// </summary>
    public sealed class ProfileStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _path;

        public List<Profile> Profiles { get; private set; } = new List<Profile>();
        public string ActiveProfileName { get; set; }
        public bool AutoSwitch { get; set; } = true;

        public static string ConfigDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrecisionCurve");

        public static string DefaultPath => Path.Combine(ConfigDirectory, "settings.json");

        public ProfileStore(string path = null)
        {
            _path = path ?? DefaultPath;
        }

        public Profile ActiveProfile
        {
            get
            {
                Profile match = Profiles.FirstOrDefault(
                    p => string.Equals(p.Name, ActiveProfileName, StringComparison.OrdinalIgnoreCase));
                return match ?? Profiles.FirstOrDefault();
            }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    Seed();
                    return;
                }

                SettingsFile file = JsonSerializer.Deserialize<SettingsFile>(
                    File.ReadAllText(_path), JsonOptions);

                if (file == null || file.Profiles == null || file.Profiles.Count == 0)
                {
                    Seed();
                    return;
                }

                Profiles = file.Profiles.Where(p => p != null).ToList();
                ActiveProfileName = file.ActiveProfile;
                AutoSwitch = file.AutoSwitch;

                foreach (Profile profile in Profiles)
                {
                    Normalize(profile);
                }

                if (ActiveProfile == null)
                {
                    ActiveProfileName = Profiles[0].Name;
                }
            }
            catch (Exception)
            {
                BackupCorruptFile();
                Seed();
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ConfigDirectory);

            var file = new SettingsFile
            {
                ActiveProfile = ActiveProfile?.Name,
                AutoSwitch = AutoSwitch,
                Profiles = Profiles
            };

            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(file, JsonOptions));

            // Write-then-replace: a crash mid-save cannot destroy the profiles.
            if (File.Exists(_path))
            {
                File.Replace(temp, _path, null);
            }
            else
            {
                File.Move(temp, _path);
            }
        }

        public void Add(Profile profile)
        {
            if (profile == null) return;
            Profile existing = Profiles.FirstOrDefault(
                p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                profile.Name = profile.Name + " (copy)";
            }
            Profiles.Add(profile);
        }

        public void Remove(Profile profile)
        {
            if (profile == null || Profiles.Count <= 1) return;
            Profiles.Remove(profile);
            if (string.Equals(ActiveProfileName, profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                ActiveProfileName = Profiles[0].Name;
            }
        }

        /// <summary>Finds the profile that claims the given process, if any.</summary>
        public Profile MatchProcess(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return null;

            return Profiles.FirstOrDefault(p => p.Processes != null &&
                p.Processes.Any(name => string.Equals(name, processName, StringComparison.OrdinalIgnoreCase)));
        }

        private void Seed()
        {
            Profiles = new List<Profile>
            {
                Profile.FromPreset("Unity", "Desktop"),
                Profile.FromPreset("FPS Flick", "FPS"),
                Profile.FromPreset("Sniper", "Sniper")
            };
            ActiveProfileName = "Desktop";
            AutoSwitch = true;
            Save();
        }

        private void BackupCorruptFile()
        {
            try
            {
                if (File.Exists(_path))
                {
                    File.Copy(_path, _path + ".bad", true);
                }
            }
            catch (Exception)
            {
                // Nothing useful to do; the defaults are already being seeded.
            }
        }

        private static void Normalize(Profile profile)
        {
            profile.Curve ??= new CurveParameters();
            profile.Curve.Clamp();
            profile.Stages ??= Profile.DefaultStages();
            profile.Snap ??= new SnapSettings();
            profile.Processes ??= new List<string>();

            if (profile.Stages.Count == 0)
            {
                profile.Stages = Profile.DefaultStages();
            }

            profile.ActiveStage = Math.Clamp(profile.ActiveStage, 0, profile.Stages.Count - 1);

            if (profile.HardwareDpi < 50 || profile.HardwareDpi > 32000)
            {
                profile.HardwareDpi = 800;
            }
        }
    }
}
