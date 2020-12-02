using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpPcap;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Sys_Info_LocalSVC
{
    public class Program
    {
        public static async Task Main()
        {
            await CreateHostBuilder().Build().RunAsync();
        }

        public static IHostBuilder CreateHostBuilder()
        {
            return Host.CreateDefaultBuilder().UseWindowsService().ConfigureServices((hostContext, services) =>
            {
                services.AddHostedService<Worker>();
                services.AddSingleton<IHostedService, RecureHostedService>();
            });
        }
    }

    public class RecureHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            ServiceStart(true);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {   
            ServiceStart(false);
            return Task.CompletedTask;
        }

        private static void ServiceStart(bool starting)
        {
            try
            {
                Stopwatch watch = new Stopwatch();
                watch.Start();
                string message;
                if (starting)
                {
                    message = $"Local service started at {DateTime.Now}";
                }
                else
                {
                    message = $"Local service has been shutdown at {DateTime.Now}";

                    for (int i = 0; i < Worker.devices.Count; i++)
                    {
                        ICaptureDevice device = Worker.devices[i];
                        device.StopCapture();
                        device.Close();
                    }
                }
                watch.Stop();
                watch.LogTime("Program", starting ? "StartAsync" : "StopAsync",message).Wait();
            }
            catch (Exception err)
            {
                err.LogErrors("Program", "ServiceStart").Wait();
            }
        }
    }
}
