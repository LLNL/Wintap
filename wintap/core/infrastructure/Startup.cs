using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using gov.llnl.wintap.core.api;
using Microsoft.Extensions.FileProviders;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace gov.llnl.wintap.core.infrastructure
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSignalR();  // Add SignalR services
                                    // Add other services like MVC, if neede
            services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", builder =>
                {
                    builder.AllowAnyOrigin() // Allows all origins
                           .AllowAnyMethod() // Allows all methods
                           .AllowAnyHeader(); // Allows all headers
                });
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            System.Diagnostics.Debugger.Launch();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();  // Use routing middleware
            app.UseCors("AllowAll");
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<ExplorerHub>("/api/ExplorerHub");  
                endpoints.MapHub<WorkbenchHub>("/api/WorkbenchHub");
                endpoints.MapHub<InferenceHub>("/api/InferenceHub");
                endpoints.MapHub<MyHub>("/myhub");
            });


            app.UseFileServer(new FileServerOptions
            {
                FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "Workbench")),
                RequestPath = "", // Use an empty string to serve files at the root path
                EnableDirectoryBrowsing = true
            });

            // Fallback or default handler
            app.Run(async (context) =>
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Page Not Found");
            });

        }
    }

}


