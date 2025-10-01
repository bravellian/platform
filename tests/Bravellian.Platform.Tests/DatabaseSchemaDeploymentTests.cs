using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;
using Bravellian.Platform;

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
                EnableSchemaDeployment = true 
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
                EnableSchemaDeployment = false 
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
        public void AddSqlScheduler_WithSchemaDeploymentEnabled_RegistersSchemaService()
        {
            // Arrange
            var services = new ServiceCollection();
            var options = new SqlSchedulerOptions 
            { 
                ConnectionString = "Server=.;Database=TestDb;Integrated Security=true;",
                EnableSchemaDeployment = true 
            };

            // Act
            services.AddSqlScheduler(options);

            // Assert
            var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
            var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));
            
            Assert.NotNull(schemaCompletionDescriptor);
            Assert.NotNull(hostedServiceDescriptor);
        }

        [Fact]
        public void MultipleServices_WithSchemaDeploymentEnabled_RegistersSingleSchemaService()
        {
            // Arrange
            var services = new ServiceCollection();
            var outboxOptions = new SqlOutboxOptions 
            { 
                ConnectionString = "Server=.;Database=TestDb;Integrated Security=true;",
                EnableSchemaDeployment = true 
            };
            var schedulerOptions = new SqlSchedulerOptions 
            { 
                ConnectionString = "Server=.;Database=TestDb;Integrated Security=true;",
                EnableSchemaDeployment = true 
            };

            // Act
            services.AddSqlOutbox(outboxOptions);
            services.AddSqlScheduler(schedulerOptions);

            // Assert
            var schemaCompletionDescriptors = services.Where(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
            var hostedServiceDescriptors = services.Where(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));
            
            Assert.Single(schemaCompletionDescriptors); // Only one instance should be registered
            Assert.Single(hostedServiceDescriptors); // Only one hosted service should be registered
        }
    }
}