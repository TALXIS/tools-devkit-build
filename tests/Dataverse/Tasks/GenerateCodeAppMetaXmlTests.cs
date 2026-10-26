using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TALXIS.DevKit.Build.Dataverse.Tasks.Tests.Support;
using Xunit;

namespace TALXIS.DevKit.Build.Dataverse.Tasks.Tests
{
    /// <summary>
    /// Power Apps opens the first CodeAppPackageUri as the app's page. The list used to follow file-system order,
    /// so on Linux, where Vite's public/ files are written before index.html, the app opened as its favicon.
    /// </summary>
    public class GenerateCodeAppMetaXmlTests : IDisposable
    {
        private const string Base = "/CanvasApps/pub_app_CodeAppPackages/";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "codeappmeta-" + Guid.NewGuid().ToString("N"));
        private readonly FakeBuildEngine _engine = new FakeBuildEngine();

        public void Dispose() => Directory.Delete(_root, true);

        [Fact]
        public void Entry_point_comes_first_and_the_rest_is_sorted_whatever_order_the_files_were_written_in()
        {
            // The order Vite writes them in: public/ first, then the bundle, then the page
            WriteConfig(null);
            WriteDist("power-apps.svg", "assets/index-b.js", "assets/index-a.css", "index.html");

            Assert.True(NewTask().Execute(), string.Join(Environment.NewLine, _engine.Errors));

            Assert.Equal(new[]
            {
                Base + "index.html_ContentType_text/html",
                Base + "assets/index-a.css_ContentType_text/css",
                Base + "assets/index-b.js_ContentType_application/javascript",
                Base + "power-apps.svg_ContentType_image/svg+xml",
            }, PackageUris());
        }

        [Fact]
        public void Entry_point_is_taken_from_build_entry_point()
        {
            WriteConfig("./app.html");
            WriteDist("index.html", "app.html");

            Assert.True(NewTask().Execute(), string.Join(Environment.NewLine, _engine.Errors));

            Assert.Equal(Base + "app.html_ContentType_text/html", PackageUris().First());
        }

        [Fact]
        public void Only_the_exact_entry_point_comes_first_when_another_file_differs_only_in_case()
        {
            // Possible on a case-sensitive file system; ordinal order alone would put Index.html first
            WriteConfig(null);
            WriteDist("Index.html", "index.html");
            // A case-insensitive file system (Windows, macOS) holds only one of them: nothing to order there
            if (Directory.GetFiles(Path.Combine(_root, "dist")).Length < 2) return;

            Assert.True(NewTask().Execute(), string.Join(Environment.NewLine, _engine.Errors));

            Assert.Equal(new[]
            {
                Base + "index.html_ContentType_text/html",
                Base + "Index.html_ContentType_text/html",
            }, PackageUris());
        }

        [Fact]
        public void Missing_entry_point_fails_the_build()
        {
            WriteConfig(null);
            WriteDist("power-apps.svg", "assets/index.js");

            Assert.False(NewTask().Execute());
            Assert.Contains(_engine.Errors, e => e.Contains("index.html"));
        }

        private GenerateCodeAppMetaXml NewTask() => new GenerateCodeAppMetaXml
        {
            BuildEngine = _engine,
            ConfigPath = Path.Combine(_root, "power.config.json"),
            AppSchemaName = "pub_app",
            OutputPath = Path.Combine(_root, "out", "pub_app.meta.xml"),
        };

        private void WriteConfig(string buildEntryPoint)
        {
            Directory.CreateDirectory(_root);
            var entry = buildEntryPoint == null ? "" : $", \"buildEntryPoint\": \"{buildEntryPoint}\"";
            File.WriteAllText(Path.Combine(_root, "power.config.json"), $"{{ \"appDisplayName\": \"App\", \"buildPath\": \"./dist\"{entry} }}");
        }

        private void WriteDist(params string[] files)
        {
            foreach (var file in files)
            {
                var path = Path.Combine(_root, "dist", file);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "");
            }
        }

        private string[] PackageUris() =>
            XDocument.Load(Path.Combine(_root, "out", "pub_app.meta.xml"))
                .Descendants("CodeAppPackageUri").Select(e => e.Value).ToArray();
    }
}
