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

/// <summary>
/// Defines the PostgreSQL collection that all database integration tests belong to.
/// Tests in this collection will share the same PostgreSQL container but get individual databases.
/// </summary>
[CollectionDefinition(Name)]
[Trait("RequiresDocker", "true")]
public class PostgresCollection : ICollectionFixture<PostgresCollectionFixture>
{
    public const string Name = "Postgres Collection";
}


