using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using bolt.cli;
using bolt.module.solution;
using bolt.module.canvas;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using bolt.system;
using System.Globalization;
using System;

public class InvokeSolutionPackager : Task
{
	[Required]
	public string Action { get; set; }

	public string PackageType { get; set; }

	public string SolutionRootDirectory { get; set; }

	[Required]
	public string PathToZipFile { get; set; }

	public string ErrorLevel { get; set; } = TraceLevel.Info.ToString();

	public string LogFilePath { get; set; }

	public string MappingFilePath { get; set; }

	public bool Localize { get; set; }

	public string LocalTemplate { get; set; }



	public override bool Execute()
	{
		ServiceCollection serviceCollection = new ServiceCollection();
		serviceCollection.AddSingleton<ISessionConfig>(new SessionConfig(false, false, true));
		serviceCollection.AddSingleton<IOutputWindow, ConsoleOutput>();
		serviceCollection.AddSingleton<ICommandProgress, OutputWindowCommandProgress>();
		serviceCollection.AddSingleton((Func<IServiceProvider, ILocalizedStrings<LocString>>)((IServiceProvider _) => new LocalizedStrings<LocString>(new CultureInfo("en-US"), "pac/loc")));

		IModule canvasModule = new CanvasModule();
		canvasModule.AddServices(serviceCollection);

		ServiceProvider services = serviceCollection.BuildServiceProvider();

		var loggerProvider = new MSBuildLoggerProvider(Log);
        ILoggerFactory loggerFactory = new LoggerFactory(new[] { loggerProvider });

		ISolutionPackagerProvider solutionPackager = new SolutionPackagerProvider(services.GetRequiredService<ICanvasPacker>(), loggerFactory.CreateLogger<SolutionPackagerProvider>(), services.GetRequiredService<ICommandProgress>(), services.GetRequiredService<ILocalizedStrings<LocString>>());
		SolutionPackagerSettings solutionPackagerSettings = new SolutionPackagerSettings
		{
			ZipFile = PathToZipFile,
			Folder = SolutionRootDirectory,
			PackageType = string.IsNullOrEmpty(PackageType) ? SolutionPackageType.Managed : (SolutionPackageType)Enum.Parse(typeof(SolutionPackageType), PackageType),
			LogFile = string.IsNullOrEmpty(LogFilePath) ? "SolutionPackager.log" : LogFilePath,
			ErrorLevel = (TraceLevel)Enum.Parse(typeof(TraceLevel), ErrorLevel),
			SingleComponent = "None",
			//AllowDeletes = command.GetSwitchArgumentValue("--allowDelete"),
			//AllowWrites = command.GetSwitchArgumentValue("--allowWrite"),
			//Clobber = command.GetSwitchArgumentValue("--clobber"),
			MappingFile = MappingFilePath,
			LocaleTemplate = LocalTemplate,
			Localize = Localize,
			//UseLcid = command.GetSwitchArgumentValue("--useLcid"),
			//UseUnmanagedFileForManaged = command.GetSwitchArgumentValue("--useUnmanagedFileForMissingManaged"),
			//DisablePluginTypeNameRemap = command.GetSwitchArgumentValue("--disablePluginRemap"),
			IsCanvasProcessingEnabled = true
		};
		solutionPackager.PackSolution(solutionPackagerSettings);

		return true;
	}

}
