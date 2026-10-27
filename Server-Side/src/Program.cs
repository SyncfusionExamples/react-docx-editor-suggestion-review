using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Syncfusion.EJ2.SpellChecker;

namespace EJ2APIServices
{
    public class Program
    {
        // Shared dictionary path used by the DocumentEditorController
        internal static string path;

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // ----- Spell check dictionary initialization (previously in Startup constructor) -----
            path = builder.Configuration["SPELLCHECK_DICTIONARY_PATH"];
            string jsonFileName = builder.Configuration["SPELLCHECK_JSON_FILENAME"];
            // check the spell check dictionary path environment variable value and assign default data folder
            // if it is null.
            path = string.IsNullOrEmpty(path) ? Path.Combine(builder.Environment.ContentRootPath, "App_Data") : Path.Combine(builder.Environment.ContentRootPath, path);
            // Set the default spellcheck.json file if the json filename is empty.
            jsonFileName = string.IsNullOrEmpty(jsonFileName) ? Path.Combine(path, "spellcheck.json") : Path.Combine(path, jsonFileName);
            if (File.Exists(jsonFileName))
            {
                string jsonImport = File.ReadAllText(jsonFileName);
                List<DictionaryData> spellChecks = JsonConvert.DeserializeObject<List<DictionaryData>>(jsonImport);
                List<DictionaryData> spellDictCollection = new List<DictionaryData>();
                string personalDictPath = null;
                // construct the dictionary file path using customer provided path and dictionary name
                if (spellChecks != null)
                {
                    foreach (var spellCheck in spellChecks)
                    {
                        spellDictCollection.Add(new DictionaryData(spellCheck.LanguadeID, Path.Combine(path, spellCheck.DictionaryPath), Path.Combine(path, spellCheck.AffixPath)));
                        personalDictPath = Path.Combine(path, spellCheck.PersonalDictPath);
                    }
                }
                SpellChecker.InitializeDictionaries(spellDictCollection, personalDictPath, 3);
            }

            // ----- Services (previously in Startup.ConfigureServices) -----
            builder.Services.AddControllers();
            builder.Services.AddMemoryCache();
            builder.Services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = null;
            });

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("MyPolicy", corsBuilder =>
                {
                    corsBuilder.AllowAnyOrigin()
                                .AllowAnyMethod()
                                .AllowAnyHeader();
                });
            });

            builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Optimal);
            builder.Services.AddResponseCompression();

            var app = builder.Build();

            // Must be registered FIRST so all subsequent middleware (routing,
            // static files, fallback) sees the path with the prefix stripped:
            // /compare-configure-.../api/documenteditor/Import -> /api/documenteditor/Import
            app.UsePathBase("/compare-configure-editable-regions-react-docx-editor");

            // ----- Middleware pipeline (previously in Startup.Configure) -----
            //Register Syncfusion license
            string licenseKey = string.Empty;
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(licenseKey);

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }
            app.UsePathBase("/react-docx-editor-suggestion-review");
            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseAuthorization();
            app.UseCors("MyPolicy");
            app.UseResponseCompression();
            app.UseDefaultFiles();
            app.UseStaticFiles();
            // SPA fallback: serve index.html for client-side routes like
            // /compare-configure-.../document-protection (deep links/refresh).
            app.MapFallbackToFile("index.html");
            app.MapControllers().RequireCors("MyPolicy");

            app.Run();
        }
    }
}
