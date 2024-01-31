using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Diagnostics;
using System.IO;

public class InvokeSolutionPackager : Task
{
	[Required]
	public string Action { get; set; }

	public string PackageType { get; set; }

	public string SolutionRootDirectory { get; set; }

	[Required]
	public string PathToZipFile { get; set; }

	public string ErrorLevel { get; set; } = "Info";

	public string LogFilePath { get; set; }

	public string MappingFilePath { get; set; }

	public bool Localize { get; set; }

	public string LocalTemplate { get; set; }

	private string PACFilePath =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "pac");

	public override bool Execute()
	{
		if (!File.Exists(PACFilePath))
		{
			Log.LogError("The pac tool is not found. Please install it using 'dotnet tool install --global Microsoft.PowerApps.CLI.Tool'");
			return false;
		}

		var args = BuildArguments();
		if (string.IsNullOrWhiteSpace(args))
		{
			Log.LogError("Failed to build arguments for pac command.");
			return false;
		}

		return RunCommand(PACFilePath, args);
	}

	private string BuildArguments()
	{
		string args = string.Empty;

		switch (Action.ToLower())
		{
			case "pack":
				args += "solution pack";
				break;
			case "unpack":
				args += "solution unpack";
				break;
			default:
				Log.LogError($"Unsupported action: {Action}");
				return null;
		}

		args += $" --zipfile \"{PathToZipFile}\"";
		args += $" --folder \"{SolutionRootDirectory}\"";
		args += $" --errorlevel {ErrorLevel}";

		if (!string.IsNullOrWhiteSpace(PackageType))
			args += $" --packagetype {PackageType}";

		if (!string.IsNullOrWhiteSpace(LogFilePath))
			args += $" --log \"{LogFilePath}\"";

		if (!string.IsNullOrWhiteSpace(MappingFilePath))
			args += $" --map \"{MappingFilePath}\"";

		if (Localize)
		{
			args += " --localize";
			if (!string.IsNullOrWhiteSpace(LocalTemplate))
				args += $" --sourceLoc {LocalTemplate}";
		}

		args += " --processCanvasApps";

		return args;
	}

	private bool RunCommand(string fileName, string arguments)
	{
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			using (Process process = new Process { StartInfo = processStartInfo })
			{
				process.OutputDataReceived += (sender, e) =>
				{
					if (!string.IsNullOrWhiteSpace(e.Data))
					{
						Log.LogMessage(MessageImportance.High, e.Data);
					}
				};

				process.ErrorDataReceived += (sender, e) =>
				{
					if (!string.IsNullOrWhiteSpace(e.Data))
					{
						Log.LogWarning(e.Data); // Logging as warning because we'll use exit code to determine if it was an error.
					}
				};

				process.Start();
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();
				process.WaitForExit();

				if (process.ExitCode != 0)
				{
					Log.LogError($"The PAC CLI command exited with code {process.ExitCode}. Inspect the log located at {LogFilePath}.");
					return false;
				}

				return true;
			}
		}
		catch (Exception ex)
		{
			Log.LogError($"Failed to run the PAC CLI command. Error: {ex.Message}");
			return false;
		}
	}
}
