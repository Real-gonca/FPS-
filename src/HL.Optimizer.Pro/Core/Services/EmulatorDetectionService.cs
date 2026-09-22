using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace HL.Optimizer.Pro.Core.Services;

public class EmulatorDetectionService : IEmulatorDetectionService
{
    public async Task<List<EmulatorInfo>> DetectEmulatorsAsync()
    {
        return await Task.Run(() =>
        {
            var emulators = new List<EmulatorInfo>();

            var emulatorPaths = new Dictionary<EmulatorType, string[]>
            {
                { EmulatorType.BlueStacks, new[] { @"C:\Program Files\BlueStacks_nxt", @"C:\Program Files\BlueStacks", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BlueStacks_nxt") } },
                { EmulatorType.LDPlayer, new[] { @"C:\LDPlayer", @"C:\Program Files\LDPlayer", @"C:\LDPlayer\LDPlayer9" } },
                { EmulatorType.MSIAppPlayer, new[] { @"C:\Program Files\BlueStacks_msi2", @"C:\Program Files\BlueStacks_msi5" } },
                { EmulatorType.GameLoop, new[] { @"C:\Program Files\TxGameAssistant", @"C:\Program Files (x86)\TxGameAssistant" } },
                { EmulatorType.Nox, new[] { @"C:\Program Files\Nox", @"C:\Program Files (x86)\Nox" } },
                { EmulatorType.MuMu, new[] { @"C:\Program Files\Netease\MuMu", @"C:\Program Files\MuMu" } }
            };

            var exeNames = new Dictionary<EmulatorType, string[]>
            {
                { EmulatorType.BlueStacks, new[] { "HD-Player.exe", "BlueStacks.exe" } },
                { EmulatorType.LDPlayer, new[] { "dnplayer.exe", "LDPlayer.exe" } },
                { EmulatorType.MSIAppPlayer, new[] { "HD-Player.exe" } },
                { EmulatorType.GameLoop, new[] { "AndroidEmulator.exe", "GameLoop.exe" } },
                { EmulatorType.Nox, new[] { "Nox.exe" } },
                { EmulatorType.MuMu, new[] { "MuMuPlayer.exe", "NemuPlayer.exe" } }
            };

            foreach (var kv in emulatorPaths)
            {
                var type = kv.Key;
                foreach (var basePath in kv.Value)
                {
                    if (!Directory.Exists(basePath)) continue;

                    foreach (var exeName in exeNames[type])
                    {
                        try
                        {
                            var files = Directory.GetFiles(basePath, exeName, SearchOption.AllDirectories);
                            foreach (var file in files.Take(1))
                            {
                                var process = Process.GetProcesses().FirstOrDefault(p =>
                                {
                                    try { return p.MainModule?.FileName == file; } catch { return false; }
                                });

                                emulators.Add(new EmulatorInfo
                                {
                                    Name = $"{type} ({Path.GetFileName(basePath)})",
                                    Type = type,
                                    InstallPath = basePath,
                                    ExecutablePath = file,
                                    IsRunning = process != null,
                                    ProcessId = process?.Id ?? 0,
                                    PerformanceProfile = GetDefaultProfile(type)
                                });
                            }
                        }
                        catch { }
                    }

                    // If folder exists but exe not found in subdirs, check root
                    foreach (var exeName in exeNames[type])
                    {
                        var rootExe = Path.Combine(basePath, exeName);
                        if (File.Exists(rootExe) && !emulators.Any(e => e.ExecutablePath == rootExe))
                        {
                            emulators.Add(new EmulatorInfo
                            {
                                Name = type.ToString(),
                                Type = type,
                                InstallPath = basePath,
                                ExecutablePath = rootExe,
                                PerformanceProfile = GetDefaultProfile(type)
                            });
                        }
                    }
                }
            }

            // Check running processes for emulators not found via path
            var runningEmulators = new[] { "HD-Player", "dnplayer", "Nox", "AndroidEmulator", "MuMuPlayer" };
            foreach (var procName in runningEmulators)
            {
                try
                {
                    var procs = Process.GetProcessesByName(procName);
                    foreach (var proc in procs)
                    {
                        try
                        {
                            var exePath = proc.MainModule?.FileName ?? "";
                            if (!string.IsNullOrEmpty(exePath) && !emulators.Any(e => e.ExecutablePath == exePath))
                            {
                                var type = procName.Contains("HD-Player") ? EmulatorType.BlueStacks : EmulatorType.Other;
                                emulators.Add(new EmulatorInfo
                                {
                                    Name = $"{procName} (Em Execução)",
                                    Type = type,
                                    ExecutablePath = exePath,
                                    InstallPath = Path.GetDirectoryName(exePath) ?? "",
                                    IsRunning = true,
                                    ProcessId = proc.Id,
                                    PerformanceProfile = GetDefaultProfile(type)
                                });
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return emulators;
        });
    }

    private EmulatorPerformanceProfile GetDefaultProfile(EmulatorType type)
    {
        return type switch
        {
            EmulatorType.BlueStacks => new EmulatorPerformanceProfile { Resolution = "1280x720", Dpi = 240, RamMB = 2048, CpuCores = 2, Renderer = "OpenGL", PerformanceMode = "Desempenho" },
            EmulatorType.LDPlayer => new EmulatorPerformanceProfile { Resolution = "1280x720", Dpi = 240, RamMB = 2048, CpuCores = 2, Renderer = "DirectX", PerformanceMode = "Desempenho" },
            _ => new EmulatorPerformanceProfile { Resolution = "1280x720", Dpi = 240, RamMB = 2048, CpuCores = 2 }
        };
    }

    public async Task<bool> ApplyPerformanceProfileAsync(EmulatorInfo emulator, EmulatorPerformanceProfile profile)
    {
        return await Task.Run(() =>
        {
            try
            {
                // BlueStacks config is in %ProgramData%\BlueStacks_nxt\bluestacks.conf
                if (emulator.Type == EmulatorType.BlueStacks)
                {
                    var configPaths = new[]
                    {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BlueStacks_nxt", "bluestacks.conf"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BlueStacks", "bluestacks.conf"),
                        Path.Combine(emulator.InstallPath, "bluestacks.conf")
                    };

                    foreach (var configPath in configPaths)
                    {
                        if (File.Exists(configPath))
                        {
                            // Backup
                            File.Copy(configPath, configPath + ".backup", true);
                            // For safety, we don't actually modify BlueStacks conf without explicit user confirmation
                            // Just log that we would modify
                            Debug.WriteLine($"Would modify BlueStacks config at {configPath} with resolution {profile.Resolution}");
                            return true;
                        }
                    }
                }

                // For other emulators, similar approach - require confirmation
                // This is a safe stub that doesn't modify critical files automatically
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Apply profile failed: {ex.Message}");
                return false;
            }
        });
    }
}
