using System;
using System.IO;
using System.Xml;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class AppendCmtDataFileToImportConfig : Task
{
    [Required]
    public string ImportConfigPath { get; set; } = "";

    [Required]
    public string FileName { get; set; } = "";

    public string Lcid { get; set; } = "";

    public string UserMapFileName { get; set; } = "";

    [Output]
    public string UpdatedImportConfig { get; private set; } = "";

    public override bool Execute()
    {
        try
        {
            var importConfig = NormalizePath(ImportConfigPath);
            var fileName = (FileName ?? "").Trim();
            var lcid = (Lcid ?? "").Trim();
            var userMap = (UserMapFileName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(importConfig))
            {
                Log.LogError("ImportConfigPath is empty.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                Log.LogError("FileName is empty.");
                return false;
            }

            if (!File.Exists(importConfig))
            {
                Log.LogError($"ImportConfig file not found: {importConfig}");
                return false;
            }

            var doc = new XmlDocument
            {
                PreserveWhitespace = true
            };
            doc.Load(importConfig);

            var root = doc.DocumentElement;
            if (root == null)
            {
                Log.LogError("ImportConfig has no root element.");
                return false;
            }

            var cmtNode = root.SelectSingleNode("cmtdatafiles") as XmlElement;
            if (cmtNode == null)
            {
                cmtNode = doc.CreateElement("cmtdatafiles");
                root.AppendChild(cmtNode);
            }

            var existing = cmtNode.SelectSingleNode($"cmtdatafile[@filename='{fileName}']") as XmlElement;
            if (existing == null)
            {
                var item = doc.CreateElement("cmtdatafile");
                item.SetAttribute("filename", fileName);
                if (!string.IsNullOrWhiteSpace(lcid))
                    item.SetAttribute("lcid", lcid);
                item.SetAttribute("usermapfilename", userMap);
                cmtNode.AppendChild(item);
                Log.LogMessage(MessageImportance.Low, $"Added cmtdatafile '{fileName}' to ImportConfig.");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(lcid))
                    existing.SetAttribute("lcid", lcid);
                if (!string.IsNullOrWhiteSpace(userMap) || existing.GetAttribute("usermapfilename") == "")
                    existing.SetAttribute("usermapfilename", userMap);
            }

            doc.Save(importConfig);
            UpdatedImportConfig = importConfig;
            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        return Path.GetFullPath(path.Trim());
    }
}
