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

using Shouldly;

namespace Bravellian.Platform.Tests;

public class StartupCheckValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ThrowsWhenNameMissing(string? name)
    {
        var check = new StubCheck(name);

        Should.Throw<ArgumentException>(() => StartupCheckValidator.Validate(check));
    }

    [Fact]
    public void ValidateUniqueNames_ThrowsOnDuplicates()
    {
        var checks = new IStartupCheck[]
        {
            new StubCheck("one"),
            new StubCheck("one"),
        };

        Should.Throw<InvalidOperationException>(() => StartupCheckValidator.ValidateUniqueNames(checks));
    }

    private sealed class StubCheck : IStartupCheck
    {
        public StubCheck(string? name)
        {
            Name = name!;
        }

        public string Name { get; }

        public int Order => 0;

        public bool IsCritical => true;

        public Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
