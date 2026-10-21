using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MusicLyricApp.Core.Service;

/// <summary>Runs from a temporary copy, after the original application has exited.</summary>
public static class UpdateInstaller
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);

    public static void ApplyFiles(UpdatePlan plan)
    {
        InAppUpdateService.ValidateManifest(plan.Manifest);
        var expected = plan.Manifest.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var files = plan.Files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var name in files)
        {
            if (!expected.TryGetValue(name, out var file)) throw new InvalidDataException("安装清单无效。");
            InAppUpdateService.SafePath(plan.TargetDirectory, name);
            InAppUpdateService.VerifyFile(InAppUpdateService.SafePath(plan.StageDirectory, name), file.Sha256);
        }
        // Verify all unchanged files as well, so a broken base never receives an incomplete delta.
        foreach (var file in plan.Manifest.Files.Where(f => !files.Contains(f.Path, StringComparer.OrdinalIgnoreCase)))
            InAppUpdateService.VerifyFile(InAppUpdateService.SafePath(plan.TargetDirectory, file.Path), file.Sha256);
        var backup = Path.Combine(Path.GetDirectoryName(plan.StageDirectory)!, "backup");
        Directory.CreateDirectory(backup);
        var originals = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        void Replace(string name, string source)
        {
            var target = InAppUpdateService.SafePath(plan.TargetDirectory, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string? saved = null;
            if (File.Exists(target))
            {
                saved = InAppUpdateService.SafePath(backup, name);
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                File.Copy(target, saved, true);
            }
            var temporary = target + ".update-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(source, temporary);
                File.Move(temporary, target, true);
                originals.Add(target, saved);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        try
        {
            foreach (var name in files) Replace(name, InAppUpdateService.SafePath(plan.StageDirectory, name));
            var manifestPath = Path.Combine(Path.GetDirectoryName(plan.StageDirectory)!, "installed-manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(plan.Manifest, InAppUpdateService.JsonOptions));
            Replace("update-manifest.json", manifestPath);
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var entry in originals.Reverse())
            {
                try
                {
                    if (entry.Value == null) { if (File.Exists(entry.Key)) File.Delete(entry.Key); }
                    else File.Copy(entry.Value, entry.Key, true);
                }
                catch (Exception error) { rollbackErrors.Add(error); }
            }
            if (rollbackErrors.Count != 0)
                throw new AggregateException("更新失败，部分文件回滚未完成。原文件备份位于：" + backup,
                    new[] { failure }.Concat(rollbackErrors));
            throw;
        }
    }

    public static void Run(string planPath)
    {
        UpdatePlan? plan = null;
        try
        {
            plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planPath), InAppUpdateService.JsonOptions)
                ?? throw new InvalidDataException("更新计划无效。");
            if (plan.ParentId > 0)
            {
                try
                {
                    using var parent = Process.GetProcessById(plan.ParentId);
                    if (parent.StartTime.ToUniversalTime().Ticks == plan.ParentStartTicks && !parent.WaitForExit(120000))
                        throw new TimeoutException("原程序仍在运行，更新未执行。");
                }
                catch (ArgumentException) { } // Parent already exited.
                catch (InvalidOperationException) { }
            }
            ApplyFiles(plan);
        }
        catch (Exception error)
        {
            var log = Path.Combine(Path.GetDirectoryName(planPath)!, "update-error.txt");
            File.WriteAllText(log, error.ToString());
            if (OperatingSystem.IsWindows()) MessageBoxW(IntPtr.Zero,
                "更新未完成，已尝试恢复原文件。\n" + error.Message + "\n详细日志：" + log, "更新失败", 0x10);
        }
        finally
        {
            if (plan is { Restart: true })
            {
                var application = Path.Combine(plan.TargetDirectory, "MusicLyricApp.exe");
                if (File.Exists(application))
                    using (Process.Start(new ProcessStartInfo(application) { UseShellExecute = true, WorkingDirectory = plan.TargetDirectory })) { }
            }
        }
    }
}
