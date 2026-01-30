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


using Microsoft.Extensions.Logging;
using Xunit;

namespace Bravellian.Platform.Tests.TestUtilities;
/// <summary>
/// Test logger factory for creating test loggers with xUnit output helper.
/// </summary>
public class TestLoggerFactory : ILoggerFactory
{
    private readonly ITestOutputHelper testOutputHelper;

    public TestLoggerFactory(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new TestLogger<object>(testOutputHelper);
    }

    public void Dispose()
    {
    }
}

