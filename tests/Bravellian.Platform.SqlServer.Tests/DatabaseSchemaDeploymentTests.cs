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

using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bravellian.Platform.Tests
{
    public class DatabaseSchemaDeploymentTests
    {
        /// <summary>
        /// When AddSqlOutbox enables schema deployment, then schema completion and background services are registered.
        /// </summary>
        /// <intent>
        /// Ensure schema deployment wiring is added when enabled.
        /// </intent>
        /// <scenario>
        /// Given SqlOutboxOptions with EnableSchemaDeployment set to true.
        /// </scenario>
        /// <behavior>
        /// Then IDatabaseSchemaCompletion and DatabaseSchemaBackgroundService are registered in the service collection.
        /// </behavior>
        [Fact]
        public void AddSqlOutbox_WithSchemaDeploymentEnabled_RegistersSchemaService()
        {
            // Arrange
            var services = new ServiceCollection();
            var options = new SqlOutboxOptions
            {
                ConnectionString = "Server=.;Database=TestDb;Integrated Security=true;",
                EnableSchemaDeployment = true,
            };

            // Act
            services.AddSqlOutbox(options);

            // Assert
            var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
            var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

            Assert.NotNull(schemaCompletionDescriptor);
            Assert.NotNull(hostedServiceDescriptor);
        }

        /// <summary>
        /// When AddSqlOutbox disables schema deployment, then schema completion and background services are not registered.
        /// </summary>
        /// <intent>
        /// Avoid schema deployment services when deployment is disabled.
        /// </intent>
        /// <scenario>
        /// Given SqlOutboxOptions with EnableSchemaDeployment set to false.
        /// </scenario>
        /// <behavior>
        /// Then IDatabaseSchemaCompletion and DatabaseSchemaBackgroundService are absent from the service collection.
        /// </behavior>
        [Fact]
        public void AddSqlOutbox_WithSchemaDeploymentDisabled_DoesNotRegisterSchemaService()
        {
            // Arrange
            var services = new ServiceCollection();
            var options = new SqlOutboxOptions
            {
                ConnectionString = "Server=.;Database=TestDb;Integrated Security=true;",
                EnableSchemaDeployment = false,
            };

            // Act
            services.AddSqlOutbox(options);

            // Assert
            var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
            var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

            Assert.Null(schemaCompletionDescriptor);
            Assert.Null(hostedServiceDescriptor);
        }

        /// <summary>
        /// When schema deployment is enabled, then IDatabaseSchemaCompletion and DatabaseSchemaCompletion are registered separately.
        /// </summary>
        /// <intent>
        /// Verify schema completion registrations use the expected lifetimes and factories.
        /// </intent>
        /// <scenario>
        /// Given AddSqlOutbox with schema deployment enabled.
        /// </scenario>
        /// <behavior>
        /// Then IDatabaseSchemaCompletion is a singleton factory and DatabaseSchemaCompletion is registered directly.
        /// </behavior>
        [Fact]
        public void SchemaCompletion_RegisteredSeparatelyFromBackgroundService()
        {
            // Arrange
            var services = new ServiceCollection();
            var options = new SqlOutboxOptions
            {
                ConnectionString = "Server=.;Database=TestDb;Integrated Security=true;",
                EnableSchemaDeployment = true,
            };

            // Act
            services.AddSqlOutbox(options);

            // Assert
            var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
            var databaseSchemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(DatabaseSchemaCompletion));
            var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

            Assert.NotNull(schemaCompletionDescriptor);
            Assert.NotNull(databaseSchemaCompletionDescriptor);
            Assert.NotNull(hostedServiceDescriptor);

            // The IDatabaseSchemaCompletion should be registered as a factory pointing to DatabaseSchemaCompletion
            Assert.Equal(ServiceLifetime.Singleton, schemaCompletionDescriptor.Lifetime);
            Assert.Equal(ServiceLifetime.Singleton, databaseSchemaCompletionDescriptor.Lifetime);

            // The implementation type for IDatabaseSchemaCompletion should be a factory
            Assert.Null(schemaCompletionDescriptor.ImplementationType);
            Assert.NotNull(schemaCompletionDescriptor.ImplementationFactory);

            // The DatabaseSchemaCompletion should be registered directly
            Assert.Equal(typeof(DatabaseSchemaCompletion), databaseSchemaCompletionDescriptor.ImplementationType);
        }

        /// <summary>
        /// When DatabaseSchemaCompletion is completed, then its completion task transitions to RanToCompletion.
        /// </summary>
        /// <intent>
        /// Ensure schema completion state tracking works as expected.
        /// </intent>
        /// <scenario>
        /// Given a new DatabaseSchemaCompletion instance.
        /// </scenario>
        /// <behavior>
        /// Then SchemaDeploymentCompleted is initially incomplete and becomes completed after SetCompleted.
        /// </behavior>
        [Fact]
        public void DatabaseSchemaCompletion_CoordinatesStateCorrectly()
        {
            // Arrange
            var completion = new DatabaseSchemaCompletion();

            // Act & Assert - Initial state should not be completed
            Assert.False(completion.SchemaDeploymentCompleted.IsCompleted);

            // Act - Signal completion
            completion.SetCompleted();

            // Assert - Should now be completed
            Assert.True(completion.SchemaDeploymentCompleted.IsCompleted);
            Assert.Equal(TaskStatus.RanToCompletion, completion.SchemaDeploymentCompleted.Status);
        }

        /// <summary>
        /// When multi-database list registration enables schema deployment, then schema services are registered.
        /// </summary>
        /// <intent>
        /// Ensure schema deployment services are added for list-based registration.
        /// </intent>
        /// <scenario>
        /// Given AddSqlPlatformMultiDatabaseWithList with enableSchemaDeployment set to true.
        /// </scenario>
        /// <behavior>
        /// Then IDatabaseSchemaCompletion and DatabaseSchemaBackgroundService are registered.
        /// </behavior>
        [Fact]
        public void AddSqlPlatformMultiDatabaseWithList_WithSchemaDeploymentEnabled_RegistersSchemaService()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

            var databases = new[]
            {
                new PlatformDatabase
                {
                    Name = "db1",
                    ConnectionString = "Server=localhost;Database=Db1;",
                    SchemaName = "infra",
                },
            };

            // Act
            services.AddSqlPlatformMultiDatabaseWithList(databases, enableSchemaDeployment: true);

            // Assert
            var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
            var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

            Assert.NotNull(schemaCompletionDescriptor);
            Assert.NotNull(hostedServiceDescriptor);
        }

        /// <summary>
        /// When schema verification is enabled, then the verification service is registered.
        /// </summary>
        /// <intent>
        /// Ensure schema verification wiring is added when enabled.
        /// </intent>
        /// <scenario>
        /// Given AddSqlPlatform with EnableSchemaVerification set to true.
        /// </scenario>
        /// <behavior>
        /// Then DatabaseSchemaVerificationService is registered in the service collection.
        /// </behavior>
        [Fact]
        public void AddSqlPlatform_WithSchemaVerificationEnabled_RegistersVerifier()
        {
            var services = new ServiceCollection();

            services.AddSqlPlatform("Server=.;Database=TestDb;Integrated Security=true;", options =>
            {
                options.EnableSchemaVerification = true;
            });

            var hostedServiceDescriptor = services.FirstOrDefault(
                s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaVerificationService));

            Assert.NotNull(hostedServiceDescriptor);
        }

        /// <summary>
        /// When control-plane registration enables schema deployment, then schema services are registered.
        /// </summary>
        /// <intent>
        /// Ensure schema deployment services are added for control-plane registration.
        /// </intent>
        /// <scenario>
        /// Given AddSqlPlatformMultiDatabaseWithControlPlaneAndList with EnableSchemaDeployment set to true.
        /// </scenario>
        /// <behavior>
        /// Then IDatabaseSchemaCompletion and DatabaseSchemaBackgroundService are registered.
        /// </behavior>
        [Fact]
        public void AddSqlPlatformMultiDatabaseWithControlPlaneAndList_WithSchemaDeploymentEnabled_RegistersSchemaService()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

            var databases = new[]
            {
                new PlatformDatabase
                {
                    Name = "db1",
                    ConnectionString = "Server=localhost;Database=Db1;",
                    SchemaName = "infra",
                },
            };

            var controlPlaneOptions = new PlatformControlPlaneOptions
            {
                ConnectionString = "Server=localhost;Database=ControlPlane;",
                SchemaName = "infra",
                EnableSchemaDeployment = true,
            };

            // Act
            services.AddSqlPlatformMultiDatabaseWithControlPlaneAndList(databases, controlPlaneOptions);

            // Assert
            var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
            var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

            Assert.NotNull(schemaCompletionDescriptor);
            Assert.NotNull(hostedServiceDescriptor);
        }

        /// <summary>
        /// When multi-database list registration disables schema deployment, then schema services are not registered.
        /// </summary>
        /// <intent>
        /// Avoid schema deployment services when list-based registration disables deployment.
        /// </intent>
        /// <scenario>
        /// Given AddSqlPlatformMultiDatabaseWithList with enableSchemaDeployment set to false.
        /// </scenario>
        /// <behavior>
        /// Then IDatabaseSchemaCompletion and DatabaseSchemaBackgroundService are absent.
        /// </behavior>
        [Fact]
        public void AddSqlPlatformMultiDatabaseWithList_WithSchemaDeploymentDisabled_DoesNotRegisterSchemaService()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

            var databases = new[]
            {
                new PlatformDatabase
                {
                    Name = "db1",
                    ConnectionString = "Server=localhost;Database=Db1;",
                    SchemaName = "infra",
                },
            };

            // Act
            services.AddSqlPlatformMultiDatabaseWithList(databases, enableSchemaDeployment: false);

            // Assert
            var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
            var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

            Assert.Null(schemaCompletionDescriptor);
            Assert.Null(hostedServiceDescriptor);
        }

        /// <summary>
        /// When multi-database list registration enables schema verification, then the verifier is registered.
        /// </summary>
        /// <intent>
        /// Ensure schema verification services are added for list-based registration.
        /// </intent>
        /// <scenario>
        /// Given AddSqlPlatformMultiDatabaseWithList with enableSchemaVerification set to true.
        /// </scenario>
        /// <behavior>
        /// Then DatabaseSchemaVerificationService is registered.
        /// </behavior>
        [Fact]
        public void AddSqlPlatformMultiDatabaseWithList_WithSchemaVerificationEnabled_RegistersVerifier()
        {
            var services = new ServiceCollection();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

            var databases = new[]
            {
                new PlatformDatabase
                {
                    Name = "db1",
                    ConnectionString = "Server=localhost;Database=Db1;",
                    SchemaName = "infra",
                },
            };

            services.AddSqlPlatformMultiDatabaseWithList(
                databases,
                enableSchemaDeployment: false,
                enableSchemaVerification: true);

            var hostedServiceDescriptor = services.FirstOrDefault(
                s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaVerificationService));

            Assert.NotNull(hostedServiceDescriptor);
        }

        /// <summary>
        /// When the schema snapshot manifest is expected, then the snapshot file exists on disk.
        /// </summary>
        /// <intent>
        /// Ensure the schema snapshot artifact is tracked in the repository.
        /// </intent>
        /// <scenario>
        /// Given the expected schema snapshot file path from SchemaVersionSnapshot.
        /// </scenario>
        /// <behavior>
        /// Then the snapshot file exists at the expected location.
        /// </behavior>
        [Fact]
        public void SchemaSnapshotManifest_IsTracked()
        {
            Assert.True(
                File.Exists(SchemaVersionSnapshot.SnapshotFilePath),
                $"Expected schema snapshot manifest at {SchemaVersionSnapshot.SnapshotFilePath}. " +
                "Generate it by running UPDATE_SCHEMA_SNAPSHOT=1 dotnet test --filter SchemaVersions_MatchSnapshot.");
        }
    }
}

