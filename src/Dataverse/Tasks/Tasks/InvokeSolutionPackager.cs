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

	public override bool Execute()
	{
#if NET10_0_OR_GREATER
		return ExecuteWithLibrary();
#else
		return ExecuteWithPacCli();
#endif
	}

#if NET10_0_OR_GREATER
	private bool ExecuteWithLibrary()
	{
		try
		{
			var packagerService = new TALXIS.Platform.Metadata.Packaging.SolutionPackagerService();
			var managed = string.Equals(PackageType, "Managed", StringComparison.OrdinalIgnoreCase);

			var options = new TALXIS.Platform.Metadata.Packaging.SolutionPackagerOptions
			{
				Managed = managed,
				Localize = Localize,
				UseUnmanagedFileForMissingManaged = UseUnmanagedFileForMissingManaged,
				MappingFilePath = MappingFilePath,
				LogFilePath = LogFilePath,
				SourceLocale = LocalTemplate
			};

			if (!string.IsNullOrWhiteSpace(ErrorLevel) &&
				Enum.TryParse<System.Diagnostics.TraceLevel>(ErrorLevel, true, out var traceLevel))
			{
				options.ErrorLevel = traceLevel;
			}

			switch (Action.ToLower())
			{
				case "pack":
					Log.LogMessage(MessageImportance.High, $"Packing solution from '{SolutionRootDirectory}' to '{PathToZipFile}'...");
					packagerService.Pack(SolutionRootDirectory, PathToZipFile, options);
					Log.LogMessage(MessageImportance.High, "Solution packed successfully.");
					break;
				case "unpack":
					Log.LogMessage(MessageImportance.High, $"Unpacking solution from '{PathToZipFile}' to '{SolutionRootDirectory}'...");
					packagerService.Unpack(PathToZipFile, SolutionRootDirectory, options);
					Log.LogMessage(MessageImportance.High, "Solution unpacked successfully.");
					break;
				default:
					Log.LogError($"Unsupported action: {Action}");
					return false;
			}

			return true;
		}
		catch (Exception ex)
		{
			Log.LogError($"Solution {Action.ToLower()} failed: {ex.Message}");
			return false;
		}
	}
#endif

#if !NET10_0_OR_GREATER
	private bool ExecuteWithPacCli()
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

	private string ResolvePACFilePath()
	{
		bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

		var candidates = isWindows
			? new[] { "pac.exe", "pac.cmd" }
			: new[] { "pac" };

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

		var toolsDir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".dotnet", "tools");

		foreach (var name in candidates)
		{
			var globalToolPath = Path.Combine(toolsDir, name);
			if (File.Exists(globalToolPath))
				return globalToolPath;
		}

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
			var stdoutLines = new List<string>();
			var stderrLines = new List<string>();

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
#endif
}
