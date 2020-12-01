using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace GrpcPollyDeadlineBug
{
    class Program
    {
        private static readonly StatusCode[] GRpcErrors = new[] {
            StatusCode.DeadlineExceeded,
            StatusCode.Internal,
            StatusCode.NotFound,
            StatusCode.ResourceExhausted,
            StatusCode.Unavailable,
            StatusCode.Unknown
        };

        private static readonly SocketError[] SocketErrors = new[]
        {
            SocketError.AddressNotAvailable,
            SocketError.ConnectionRefused,
            SocketError.HostNotFound,
            SocketError.HostUnreachable,
            SocketError.HostDown
        };

        public static StatusCode? GetStatusCode(HttpResponseMessage response)
        {
            if (null == response)
                return StatusCode.Unknown;

            var headers = response.Headers;

            if (!headers.Contains("grpc-status") && response.StatusCode == HttpStatusCode.OK)
                return StatusCode.OK;

            if (headers.Contains("grpc-status"))
                return (StatusCode)int.Parse(headers.GetValues("grpc-status").First());

            return null;
        }

        static async Task<int> Main(string[] args)
        {
            int appReturnCode = 0;

            try
            {
                IConfiguration config = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", true)
                    .AddEnvironmentVariables()
                    .AddCommandLine(args)
                    .Build();

                await CreateHostBuilder(args, config)
                    .Build()
                    .RunAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine("Global Exception occurred.");
                Console.WriteLine($"{e.GetType()}: {e.Message}");
                Console.WriteLine(e.StackTrace);
            }

            return appReturnCode;
        }

        public static IHostBuilder CreateHostBuilder(string[] args, IConfiguration configuration) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                })
                .ConfigureHostConfiguration(builder => { builder.AddConfiguration(configuration); })
                .UseConsoleLifetime()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<ConsoleHostedService>();
                    services.AddGrpc();

                    services.AddGrpcClient<Greeter.GreeterClient>(opt =>
                        {
                            opt.Address = new Uri("https://localhost:5001");
                        })
                        .AddPolicyHandler((services, request) =>
                        {
                            return HttpPolicyExtensions
                                .HandleTransientHttpError()
                                .Or<RpcException>(rpcException =>
                                {
                                    return true;
                                })
                                .Or<TaskCanceledException>(ex =>
                                {
                                    return true;
                                })
                                .OrResult(httpMessage =>
                                {
                                    var grpcStatus = GetStatusCode(httpMessage);
                                    var httpStatusCode = httpMessage.StatusCode;
                                    return grpcStatus != null &&
                                           ((httpStatusCode == HttpStatusCode.OK &&
                                             GRpcErrors.Contains(grpcStatus.Value)));
                                })
                                .WaitAndRetryAsync(
                                    3,
                                    (input) => TimeSpan.FromSeconds(3 + input),
                                    (outcome, timespan, retryAttempt, context) =>
                                    {
                                        var uri = request.RequestUri;
                                        var grpcStatus = GetStatusCode(outcome?.Result);

                                        services.GetService<ILogger>()?
                                            .LogError(
                                                "Request {uri} failed with {grpcStatus}. Retry in {seconds}",
                                                uri,
                                                grpcStatus,
                                                timespan);
                                    });
                        });
                });
    }
}
