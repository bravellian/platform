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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bravellian.Platform.Tests
{
    public class DatabaseSchemaDeploymentTests
    {
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

        [Fact]
        public void AddPlatformMultiDatabaseWithList_WithSchemaDeploymentEnabled_RegistersSchemaService()
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
                    SchemaName = "dbo",
                },
            };

            // Act
            services.AddPlatformMultiDatabaseWithList(databases, enableSchemaDeployment: true);

            // Assert
            var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
            var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

            Assert.NotNull(schemaCompletionDescriptor);
            Assert.NotNull(hostedServiceDescriptor);
        }

        [Fact]
        public void AddPlatformMultiDatabaseWithControlPlane_WithSchemaDeploymentEnabled_RegistersSchemaService()
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
                    SchemaName = "dbo",
                },
            };

            var controlPlaneOptions = new PlatformControlPlaneOptions
            {
                ConnectionString = "Server=localhost;Database=ControlPlane;",
                SchemaName = "dbo",
                EnableSchemaDeployment = true,
            };

            // Act
            services.AddPlatformMultiDatabaseWithControlPlaneAndList(databases, controlPlaneOptions);

            // Assert
            var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
            var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

            Assert.NotNull(schemaCompletionDescriptor);
            Assert.NotNull(hostedServiceDescriptor);
        }

        [Fact]
        public void AddPlatformMultiDatabaseWithList_WithSchemaDeploymentDisabled_DoesNotRegisterSchemaService()
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
                    SchemaName = "dbo",
                },
            };

            // Act
            services.AddPlatformMultiDatabaseWithList(databases, enableSchemaDeployment: false);

            // Assert
            var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
            var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

            Assert.Null(schemaCompletionDescriptor);
            Assert.Null(hostedServiceDescriptor);
        }
    }
}
