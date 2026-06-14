using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class GenerateGenPageFileXml : Task
{
    [Required]
    public string ProjectId { get; set; }

    [Required]
    public string SourceFilePath { get; set; }

    [Required]
    public string FileName { get; set; }

    [Required]
    public string FileType { get; set; }

    [Required]
    public string MimeType { get; set; }

    [Required]
    public string OutputDir { get; set; }

    [Output]
    public string GeneratedFileId { get; set; }

    public override bool Execute()
    {
        try
        {
            if (!File.Exists(SourceFilePath))
            {
                Log.LogError($"Source file not found: {SourceFilePath}");
                return false;
            }

            var normalizedId = ProjectId.Trim().Trim('{', '}').ToLowerInvariant();

            // Generate deterministic GUID from ProjectId + FileName
            var seed = normalizedId + "-" + FileName;
            GeneratedFileId = DeterministicGuid(seed);

            var fileDir = Path.Combine(OutputDir, GeneratedFileId);
            var fileContentDir = Path.Combine(fileDir, "filecontent");
            Directory.CreateDirectory(fileContentDir);

            // Copy source file preserving original filename
            var sourceFileName = Path.GetFileName(SourceFilePath);
            var destPath = Path.Combine(fileContentDir, sourceFileName);
            File.Copy(SourceFilePath, destPath, true);

            // Generate uxagentprojectfile.xml
            var doc = new XDocument(
                new XElement("uxagentprojectfile",
                    new XAttribute("uxagentprojectfileid", GeneratedFileId),
                    new XElement("filecontent",
                        new XAttribute("mimetype", MimeType),
                        sourceFileName),
                    new XElement("filename", FileName),
                    new XElement("filetype", FileType),
                    new XElement("iscustomizable", "1"),
                    new XElement("statecode", "0"),
                    new XElement("statuscode", "1")
                )
            );

            var xmlPath = Path.Combine(fileDir, "uxagentprojectfile.xml");

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineChars = Environment.NewLine,
                NewLineHandling = NewLineHandling.Replace
            };

            using (var writer = XmlWriter.Create(xmlPath, settings))
            {
                doc.Save(writer);
            }

            Log.LogMessage(MessageImportance.High, $"Generated GenPage file XML: {xmlPath} (FileId={GeneratedFileId})");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
    }

    private static string DeterministicGuid(string seed)
    {
        using (var md5 = MD5.Create())
        {
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
            var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return hex.Substring(0, 8) + "-"
                 + hex.Substring(8, 4) + "-"
                 + hex.Substring(12, 4) + "-"
                 + hex.Substring(16, 4) + "-"
                 + hex.Substring(20, 12);
        }
    }
}
