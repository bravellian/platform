// Copyright (c) Bravellian
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Bravellian.Platform.Tests;

internal static class SqlServerTestEnvironment
{
    private static readonly string[] SqlCmdFileNames = OperatingSystem.IsWindows()
        ? new[] { "sqlcmd.exe", "sqlcmd" }
        : new[] { "sqlcmd", "sqlcmd.exe" };

    private static readonly string[] WindowsSqlCmdFallbackPaths =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SqlCmd", "sqlcmd.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SqlCmd", "sqlcmd.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft SQL Server", "Client SDK", "ODBC", "170", "Tools", "Binn", "sqlcmd.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft SQL Server", "Client SDK", "ODBC", "170", "Tools", "Binn", "sqlcmd.exe"),
        Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? string.Empty, "SqlCmd", "sqlcmd.exe"),
        Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? string.Empty, "Microsoft SQL Server", "Client SDK", "ODBC", "170", "Tools", "Binn", "sqlcmd.exe"),
        Path.Combine("C:\\", "Program Files", "SqlCmd", "sqlcmd.exe"),
        Path.Combine("C:\\", "Program Files", "Microsoft SQL Server", "Client SDK", "ODBC", "170", "Tools", "Binn", "sqlcmd.exe"),
    };

    internal static bool IsSqlCmdAvailable()
    {
        if (OperatingSystem.IsWindows())
        {
            if (TryLocateSqlCmd(out var resolvedPath))
            {
                return CanExecuteSqlCmd(resolvedPath!);
            }

            foreach (var fallbackPath in WindowsSqlCmdFallbackPaths)
            {
                if (File.Exists(fallbackPath))
                {
                    return CanExecuteSqlCmd(fallbackPath);
                }
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        foreach (var segment in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            foreach (var fileName in SqlCmdFileNames)
            {
                var candidate = Path.Combine(segment.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return CanExecuteSqlCmd(candidate);
                }
            }
        }

        return false;
    }

    internal static bool TryLocateSqlCmd(out string? resolvedPath)
    {
        resolvedPath = null;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        foreach (var fallbackPath in WindowsSqlCmdFallbackPaths)
        {
            if (File.Exists(fallbackPath))
            {
                resolvedPath = fallbackPath;
                return true;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        foreach (var segment in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            foreach (var fileName in SqlCmdFileNames)
            {
                var candidate = Path.Combine(segment.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    resolvedPath = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CanExecuteSqlCmd(string filePath)
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    Arguments = "-?",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            if (!process.Start())
            {
                return false;
            }

            process.WaitForExit(TimeSpan.FromSeconds(5));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
