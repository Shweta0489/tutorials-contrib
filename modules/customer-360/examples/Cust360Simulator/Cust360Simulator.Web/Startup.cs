using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Couchbase.Authentication;
using Couchbase.Configuration.Client;
using Couchbase.Extensions.DependencyInjection;
using Cust360Simulator.Core;
using Cust360Simulator.Core.HomeDelivery;
using Cust360Simulator.Core.Loyalty;
using Cust360Simulator.Core.OnlineStore;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using MySql.Data.MySqlClient;
using Npgsql;

namespace Cust360Simulator.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc()
                .AddNewtonsoftJson();

            services.AddSingleton<MySqlConnection>(x =>
            {
                var mysqlConnectionString = "server=mysql;port=3306;database=inventory;user=root;password=debezium";
                return new MySqlConnection(mysqlConnectionString);
            });
            services.AddSingleton<NpgsqlConnection>(x =>
            {
                var postgresConnectionString = "User ID=postgres;Password=password;Server=loyaltydb;Port=5432;";
                return new NpgsqlConnection(postgresConnectionString);
            });
            services.AddSingleton<SQLiteConnection>(x =>
            {
                var sqliteConnectionString = "Data Source=onlineStore.db";
                return new SQLiteConnection(sqliteConnectionString);
            });
            services.AddCouchbase(x =>
            {
                x.Servers = new List<Uri> {new Uri("http://db:8091")};
                x.Username = "Administrator";
                x.Password = "password";
                x.ConnectionPool = new ConnectionPoolDefinition
                {
                    MaxSize = 10
                };
            });
            var clientConfiguration = new ClientConfiguration
            {
                Servers = new List<Uri> {new Uri("http://db:8091")}
            };
            clientConfiguration.SetAuthenticator(new PasswordAuthenticator("Administrator", "password"));
            services.AddHangfire(x => x.UseCouchbaseStorage(clientConfiguration, "customer360_hangfire"));

            services.AddTransient<HomeDeliveryRepository>();
            services.AddTransient<LoyaltyRepository>();
            services.AddTransient<LoyaltyCsvExportService>();
            services.AddTransient<LoyaltyCsvImportService>();
            services.AddTransient<EnterpriseData>();
            services.AddTransient<OnlineStoreRepository>();

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo {Title = "Customer 360 API", Version = "v1"});
            });

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env,
            LoyaltyCsvExportService loyaltyCsvExportService,
            LoyaltyCsvImportService loyaltyCsvImportService,
            ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Customer 360 API v1");
            });

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseHangfireDashboard("/hangfire", new DashboardOptions
            {
                Authorization = new [] { new AllowAnyoneAuthorization()}
            });
            app.UseHangfireServer();

            app.UseAuthorization();

            app.UseStaticFiles();

            loggerFactory.AddLog4Net();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            // ****** recurring jobs *********
            RecurringJob.AddOrUpdate("nightlyCsvExport", () => loyaltyCsvExportService.ExportCsv(), Cron.Daily(2, 0));

            RecurringJob.AddOrUpdate("nightlyCsvImport", () => loyaltyCsvImportService.ImportCsv(), Cron.Daily(3, 0));
            // *******************************
        }

        public class AllowAnyoneAuthorization : IDashboardAuthorizationFilter
        {
            public bool Authorize(DashboardContext context)
            {
                return true;
            }
        }
    }
}
