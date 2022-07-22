/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */


using Microsoft.Owin.Cors;
using Microsoft.Owin.Diagnostics;
using Owin;
//using Swashbuckle.Application;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Text;
using System.Web.Http;
using static gov.llnl.wintap.Interfaces;
using Microsoft.Owin.FileSystems;
using Microsoft.Owin.StaticFiles;

namespace gov.llnl.wintap
{
    public class OwinStartup
    {
        /// <summary>
        /// .NET web hosting via OWIN serves up the API, workbench, swagger file and the websockets layer.
        /// http://owin.org/
        /// https://docs.microsoft.com/en-us/aspnet/web-api/overview/hosting-aspnet-web-api/use-owin-to-self-host-web-api
        /// </summary>
        public void Configuration(IAppBuilder appBuilder)
        {
            //Set the Welcome page to test if Owin is hosted properly
            //appBuilder.UseWelcomePage("/welcome.html");
            //appBuilder.UseErrorPage(new ErrorPageOptions() { ShowExceptionDetails = true });

            // swagger at http://localhost:8099/swagger
            HttpConfiguration config = new HttpConfiguration();   
            // decided to thin the dependencies and NOT include swagger.  Add
            // back the nuget pkg and uncomment to bring it back.
            //config.EnableSwagger(c => c.SingleApiVersion("v1", "Wintap API").Description("methods for interacting with Wintap's embedded Esper streaming event engine.")).EnableSwaggerUi();
            
            config.Routes.MapHttpRoute(
                name: "defaultApiRoute",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
                );

            //Maps Http routes based on attributes
            config.MapHttpAttributeRoutes();

            //Enable WebApi  
            appBuilder.UseWebApi(config);
            // Enable websockets
            appBuilder.MapSignalR();
            appBuilder.UseCors(CorsOptions.AllowAll);

            //Make .App folder as the default root for the static files
            appBuilder.UseFileServer(new FileServerOptions
            {
                RequestPath = new Microsoft.Owin.PathString(string.Empty),
                //FileSystem = new PhysicalFileSystem("./App/dist/admin/"),
                FileSystem = new PhysicalFileSystem("./Workbench/"),
                EnableDirectoryBrowsing = true
            });
        }
    }
}
