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

    internal static bool IsSqlCmdAvailable()
    {
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
                    return true;
                }
            }
        }

        return false;
    }
}
