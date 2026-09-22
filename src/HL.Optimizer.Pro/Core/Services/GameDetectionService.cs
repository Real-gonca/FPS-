using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace HL.Optimizer.Pro.Core.Services;

public class GameDetectionService : IGameDetectionService
{
    private readonly IPowerService _powerService;

    public GameDetectionService(IPowerService powerService)
    {
        _powerService = powerService;
    }

    public async Task<List<GameProfile>> DetectGamesAsync()
    {
        return await Task.Run(() =>
        {
            var games = new List<GameProfile>();

            // Common game locations
            var searchPaths = new[]
            {
                @"C:\Program Files",
                @"C:\Program Files (x86)",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData"),
                @"D:\Games",
                @"E:\Games",
                @"C:\Games",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps", "common"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            };

            var knownGames = new Dictionary<string, GameType>
            {
                { "valorant", GameType.Valorant },
                { "cs2", GameType.CS2 },
                { "csgo", GameType.CS2 },
                { "fortnite", GameType.Fortnite },
                { "minecraft", GameType.Minecraft },
                { "free fire", GameType.FreeFire },
                { "bluestacks", GameType.BlueStacks },
            };

            // Scan registry for installed programs
            try
            {
                var uninstallKeys = new[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };

                foreach (var keyPath in uninstallKeys)
                {
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
                        using var key = baseKey.OpenSubKey(keyPath);
                        if (key == null) continue;

                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            try
                            {
                                using var subKey = key.OpenSubKey(subKeyName);
                                if (subKey == null) continue;
                                var displayName = subKey.GetValue("DisplayName")?.ToString() ?? "";
                                var installLocation = subKey.GetValue("InstallLocation")?.ToString() ?? "";
                                var displayIcon = subKey.GetValue("DisplayIcon")?.ToString() ?? "";

                                var lowerName = displayName.ToLower();
                                foreach (var kv in knownGames)
                                {
                                    if (lowerName.Contains(kv.Key))
                                    {
                                        games.Add(new GameProfile
                                        {
                                            Name = displayName,
                                            ExecutablePath = installLocation,
                                            Type = kv.Value,
                                            IsDetected = true
                                        });
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // Scan file system for known executables
            var exeNames = new[] { "valorant.exe", "cs2.exe", "fortnite.exe", "minecraft.exe", "HD-Player.exe", "BlueStacks.exe" };
            foreach (var basePath in searchPaths)
            {
                if (!Directory.Exists(basePath)) continue;
                try
                {
                    foreach (var exe in exeNames)
                    {
                        try
                        {
                            var files = Directory.GetFiles(basePath, exe, SearchOption.AllDirectories);
                            foreach (var file in files.Take(2))
                            {
                                var type = exe.ToLower().Contains("bluestacks") ? GameType.BlueStacks : GameType.Custom;
                                if (exe.ToLower().Contains("valorant")) type = GameType.Valorant;
                                if (exe.ToLower().Contains("cs2")) type = GameType.CS2;
                                if (exe.ToLower().Contains("fortnite")) type = GameType.Fortnite;
                                if (exe.ToLower().Contains("minecraft")) type = GameType.Minecraft;

                                if (!games.Any(g => g.ExecutablePath == file))
                                {
                                    games.Add(new GameProfile
                                    {
                                        Name = Path.GetFileNameWithoutExtension(file),
                                        ExecutablePath = file,
                                        Type = type,
                                        IsDetected = true
                                    });
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // Add custom profiles if none found
            if (games.Count == 0)
            {
                games.Add(new GameProfile { Name = "Valorant", Type = GameType.Valorant, IsDetected = false });
                games.Add(new GameProfile { Name = "CS2", Type = GameType.CS2, IsDetected = false });
                games.Add(new GameProfile { Name = "Fortnite", Type = GameType.Fortnite, IsDetected = false });
                games.Add(new GameProfile { Name = "Minecraft", Type = GameType.Minecraft, IsDetected = false });
                games.Add(new GameProfile { Name = "Free Fire (Emulador)", Type = GameType.FreeFire, IsDetected = false });
            }

            return games.DistinctBy(g => g.Name).ToList();
        });
    }

    public async Task<bool> ApplyGameBoosterAsync(GameProfile game)
    {
        try
        {
            // Set high performance power plan
            if (game.BoosterSettings.HighPerformancePowerPlan)
            {
                await _powerService.SetHighPerformanceAsync();
            }

            // Set process priority if running
            if (!string.IsNullOrEmpty(game.ExecutablePath))
            {
                var exeName = Path.GetFileName(game.ExecutablePath);
                var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName));
                foreach (var proc in processes)
                {
                    try
                    {
                        if (game.BoosterSettings.HighPriority)
                            proc.PriorityClass = ProcessPriorityClass.High;
                    }
                    catch { }
                }
            }

            // Suspend non-essential processes (simplified - just lower priority)
            if (game.BoosterSettings.SuspendNonEssentialProcesses)
            {
                var toLower = new[] { "onedrive", "searchindexer", "skype", "teams" };
                foreach (var name in toLower)
                {
                    var procs = Process.GetProcessesByName(name);
                    foreach (var p in procs)
                    {
                        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                    }
                }
            }

            return true;
        }
        catch { return false; }
    }

    public async Task<bool> RevertGameBoosterAsync(GameProfile game)
    {
        try
        {
            await _powerService.SetBalancedAsync();

            if (!string.IsNullOrEmpty(game.ExecutablePath))
            {
                var exeName = Path.GetFileName(game.ExecutablePath);
                var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName));
                foreach (var proc in processes)
                {
                    try { proc.PriorityClass = ProcessPriorityClass.Normal; } catch { }
                }
            }
            return true;
        }
        catch { return false; }
    }
}
