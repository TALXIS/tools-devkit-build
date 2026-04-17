using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

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

	public bool UseUnmanagedFileForMissingManaged { get; set; }

	private string ResolvePACFilePath()
	{
		bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

		var candidates = isWindows
			? new[] { "pac.exe", "pac.cmd" }
			: new[] { "pac" };

		// Check the standalone Power Platform CLI location first (preferred, supports latest versions)
		if (isWindows)
		{
			var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			foreach (var name in candidates)
			{
				var standalonePath = Path.Combine(localAppData, "Microsoft", "PowerAppsCLI", name);
				if (File.Exists(standalonePath))
					return standalonePath;
			}
		}

		// Check the standard global tools location
		var toolsDir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".dotnet", "tools");

		foreach (var name in candidates)
		{
			var globalToolPath = Path.Combine(toolsDir, name);
			if (File.Exists(globalToolPath))
				return globalToolPath;
		}

		// Fall back to searching PATH
		var pathEnv = Environment.GetEnvironmentVariable("PATH");
		
		if (!string.IsNullOrEmpty(pathEnv))
		{
			var separator = isWindows ? ';' : ':';
			
			foreach (var name in candidates)
			{
				var found = pathEnv.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries)
					.Select(dir => dir.Trim().Trim('"'))
					.Where(dir => dir.Length > 0 && Directory.Exists(dir))
					.Select(dir => Path.Combine(dir, name))
					.FirstOrDefault(File.Exists);

				if (found != null)
					return found;
			}
		}

		return null;
	}

	public override bool Execute()
	{
		var pacPath = ResolvePACFilePath();
		if (pacPath == null)
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

		return RunCommand(pacPath, args);
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

		if (UseUnmanagedFileForMissingManaged)
			args += " --useUnmanagedFileForMissingManaged";

		args += " --processCanvasApps";

		return args;
	}

	private bool RunCommand(string fileName, string arguments)
	{
		try
		{
			var stdoutLines = new System.Collections.Generic.List<string>();
			var stderrLines = new System.Collections.Generic.List<string>();

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
						stdoutLines.Add(e.Data);
						Log.LogMessage(MessageImportance.High, e.Data);
					}
				};

				process.ErrorDataReceived += (sender, e) =>
				{
					if (!string.IsNullOrWhiteSpace(e.Data))
					{
						stderrLines.Add(e.Data);
						Log.LogWarning(e.Data);
					}
				};

				process.Start();
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();
				process.WaitForExit();

				if (process.ExitCode != 0)
				{
					var errorDetails = stderrLines.Count > 0
						? string.Join(Environment.NewLine, stderrLines)
						: null;

					var outputErrors = stdoutLines
						.Where(l => l.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
							|| l.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0
							|| l.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
							|| l.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0
							|| l.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
							|| l.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0)
						.ToList();

					if (outputErrors.Count > 0)
					{
						var relevantOutput = string.Join(Environment.NewLine, outputErrors);
						errorDetails = errorDetails != null
							? errorDetails + Environment.NewLine + relevantOutput
							: relevantOutput;
					}

					if (!string.IsNullOrWhiteSpace(errorDetails))
					{
						Log.LogError($"PAC solution {Action.ToLower()} failed (exit code {process.ExitCode}):{Environment.NewLine}{errorDetails}");
					}
					else
					{
						Log.LogError($"PAC solution {Action.ToLower()} failed (exit code {process.ExitCode}). No error details captured from output.");
					}

					if (!string.IsNullOrWhiteSpace(LogFilePath) && File.Exists(LogFilePath))
					{
						Log.LogError($"Full log available at: {LogFilePath}");
					}

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
