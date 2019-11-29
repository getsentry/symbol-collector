using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SymbolCollector.Server.Services;

namespace SymbolCollector.Server
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services) => services.AddGrpc();

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(e => e.MapGrpcService<SymbolService>());
        }
    }
}
