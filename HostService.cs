using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GrpcPollyDeadlineBug
{
    public class ConsoleHostedService : IHostedService
    {
        private readonly IHostApplicationLifetime _appLifetime;
        private int? _exitCode;
        private readonly Greeter.GreeterClient _greeterClient;
        private readonly ILogger<ConsoleHostedService> _logger;

        public ConsoleHostedService(IHostApplicationLifetime appLifetime, Greeter.GreeterClient greeterClient, ILogger<ConsoleHostedService> logger)
        {
            _appLifetime = appLifetime;
            _greeterClient = greeterClient;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _appLifetime.ApplicationStarted.Register(() =>
            {
                Task.Run(async () =>
                {
                    try
                    {
                        var result = await _greeterClient.SayHelloAsync(
                            new HelloRequest {Name = "ME"},
                            deadline: DateTime.Now.AddSeconds(1).ToUniversalTime());
                            
                       
                        //var result = await _greeterClient.SayHello("ME");
                        
                        _logger.LogInformation(result.Message);
                        _exitCode = 0;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled exception.");
                        _exitCode = 1;
                    }
                    finally
                    {
                        // Stop the application once the work is done
                        _appLifetime.StopApplication();
                    }
                });
            });

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
