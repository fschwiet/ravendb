//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Configuration.Install;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.ServiceProcess;
using log4net;
using log4net.Appender;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using NDesk.Options;
using Raven.Database;
using Raven.Database.Config;
using Raven.Database.Extensions;
using Raven.Database.Impl.Logging;
using Raven.Http;

namespace Raven.Server
{
    public static class Program
    {
        private static void Main(string[] args)
        {
            if (Environment.UserInteractive)
            {
                try
                {
                    InteractiveRun(args);
                }
                catch (ReflectionTypeLoadException e)
                {
                    EmitWarningInRed();

                    Console.WriteLine(e);
                    foreach (var loaderException in e.LoaderExceptions)
                    {
                        Console.WriteLine("- - - -");
                        Console.WriteLine(loaderException);
                    }

                    WaitForUserInputAndExitWithError();
                }
                catch (Exception e)
                {
                    EmitWarningInRed(); 
                    
                    Console.WriteLine(e);

                    WaitForUserInputAndExitWithError();
                }
            }
            else
            {
                // no try catch here, we want the exception to be logged by Windows
                ServiceBase.Run(new RavenService());
            }
        }

        private static void WaitForUserInputAndExitWithError()
        {
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey(true);
            Environment.Exit(-1);
        }

        private static void EmitWarningInRed()
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("A critical error occurred while starting the server. Please see the exception details bellow for more details:");
            Console.ForegroundColor = old;
        }

        private static void InteractiveRun(string[] args)
        {
        	string backupLocation = null;
        	string restoreLocation = null;
        	Action actionToTake = null;
        	bool launchBrowser = false;

        	OptionSet optionSet = null;
        	optionSet = new OptionSet
        	{
        		{"install", "Installs the RavenDB service", key => actionToTake= () => AdminRequired(InstallAndStart, key)},
        		{"uninstall", "Uninstalls the RavenDB service", key => actionToTake= () => AdminRequired(EnsureStoppedAndUninstall, key)},
        		{"start", "Starts the RavenDB servce", key => actionToTake= () => AdminRequired(StartService, key)},
        		{"restart", "Restarts the RavenDB service", key => actionToTake= () => AdminRequired(RestartService, key)},
        		{"stop", "Stops the RavenDB service", key => actionToTake= () => AdminRequired(StopService, key)},
        		{"ram", "Run RavenDB in RAM only", key =>
        		{
					actionToTake = () => RunInDebugMode(AnonymousUserAccessMode.All, new RavenConfiguration
					{
						Settings =
							{
								{"Raven/RunInMemory","true"} 
							}
					}, launchBrowser);		
        		}},
        		{"debug", "Runs RavenDB in debug mode", key =>
        		{
					actionToTake = () => RunInDebugMode(null, new RavenConfiguration(), launchBrowser);
        		}},
				{"browser|launchbrowser", "After the server starts, launches the browser", key => launchBrowser = true},
        		{"help", "Help about the command line interface", key =>
        		{
					actionToTake = () => PrintUsage(optionSet);
        		}},
        		{"restore", 
        			"Restores a RavenDB database from backup",
        			key => actionToTake = () =>
        			{
        				if(backupLocation == null || restoreLocation == null)
        				{
        					throw new OptionException("when using restore, source and destination must be specified", "restore");
        				}
        				RunRestoreOperation(backupLocation, restoreLocation);
        			}},
        		{"dest=|destination=", "The {0:path} of the new new database", value => restoreLocation = value},
        		{"src=|source=", "The {0:path} of the backup", value => backupLocation = value},
        	};


        	try
        	{
				if(args.Length == 0) // we default to executing in debug mode 
					args = new[]{"--debug"};

        		optionSet.Parse(args);
        	}
        	catch (Exception e)
        	{
        		Console.WriteLine(e.Message);
        		PrintUsage(optionSet);
        		return;
        	}

			if (actionToTake == null)
				actionToTake = () => PrintUsage(optionSet);
			
			actionToTake();
			
        }

        private static void RunRestoreOperation(string backupLocation, string databaseLocation)
        {
            try
            {
                var ravenConfiguration = new RavenConfiguration();
                if(File.Exists(Path.Combine(backupLocation, "Raven.ravendb")))
                {
                    ravenConfiguration.DefaultStorageTypeName =
                        "Raven.Storage.Managed.TransactionalStorage, Raven.Storage.Managed";
                }
                else if(Directory.Exists(Path.Combine(backupLocation, "new")))
                {
                    ravenConfiguration.DefaultStorageTypeName = "Raven.Storage.Esent.TransactionalStorage, Raven.Storage.Esent";

                }
                DocumentDatabase.Restore(ravenConfiguration, backupLocation, databaseLocation);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private static void AdminRequired(Action actionThatMayRequiresAdminPrivileges, string cmdLine)
        {
            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            if (principal.IsInRole(WindowsBuiltInRole.Administrator) == false)
            {
                if (RunAgainAsAdmin(cmdLine))
                    return;
            }
            actionThatMayRequiresAdminPrivileges();
        }

        private static bool RunAgainAsAdmin(string cmdLine)
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    Arguments = "--" + cmdLine,
                    FileName = Assembly.GetExecutingAssembly().Location,
                    Verb = "runas",
                });
                if (process != null)
                    process.WaitForExit();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetArgument(string[] args)
        {
            if (args.Length == 0)
                return "debug";
            if (args[0].StartsWith("/") == false)
                return "help";
            return args[0].Substring(1);
        }

        private static void RunInDebugMode(AnonymousUserAccessMode? anonymousUserAccessMode, RavenConfiguration ravenConfiguration, bool lauchBrowser)
        {
        	ConfigureDebugLogging();

        	NonAdminHttp.EnsureCanListenToWhenInNonAdminContext(ravenConfiguration.Port);
            if (anonymousUserAccessMode.HasValue)
                ravenConfiguration.AnonymousUserAccessMode = anonymousUserAccessMode.Value;
			while (RunServerInDebugMode(ravenConfiguration, lauchBrowser))
            {
            	lauchBrowser = false;
            }
        }

    	private static void ConfigureDebugLogging()
    	{
			if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log4net.config")))
				return;// that overrides the default config

			var loggerRepository = LogManager.GetRepository(typeof(HttpServer).Assembly);
			
			var patternLayout = new PatternLayout(PatternLayout.DefaultConversionPattern);
    		var consoleAppender = new ConsoleAppender
    		                      	{
    		                      		Layout = patternLayout,
    		                      	};
    		consoleAppender.ActivateOptions();
    		((Logger)loggerRepository.GetLogger(typeof(HttpServer).FullName)).AddAppender(consoleAppender);
    		var fileAppender = new RollingFileAppender
    		                   	{
    		                   		AppendToFile = false,
    		                   		File = "Raven.Server.log",
    		                   		Layout = patternLayout,
    		                   		MaxSizeRollBackups = 3,
    		                   		MaximumFileSize = "1024KB",
    		                   		StaticLogFileName = true,
									LockingModel = new FileAppender.MinimalLock()
    		                   	};
    		fileAppender.ActivateOptions();

    		var asyncBufferingAppender = new AsyncBufferingAppender();
    		asyncBufferingAppender.AddAppender(fileAppender);

    		((Hierarchy) loggerRepository).Root.AddAppender(asyncBufferingAppender);
    		loggerRepository.Configured = true;
    	}

    	private static bool RunServerInDebugMode(RavenConfiguration ravenConfiguration, bool lauchBrowser)
    	{
    		var sp = Stopwatch.StartNew();
            using (var server = new RavenDbServer(ravenConfiguration))
            {
				sp.Stop();
                var path = Path.Combine(Environment.CurrentDirectory, "default.raven");
                if (File.Exists(path))
                {
                    Console.WriteLine("Loading data from: {0}", path);
                    Smuggler.Smuggler.ImportData(ravenConfiguration.ServerUrl, path);
                }

                Console.WriteLine("Raven is ready to process requests. Build {0}, Version {1}", DocumentDatabase.BuildVersion, DocumentDatabase.ProductVersion);
            	Console.WriteLine("Server started in {0:#,#} ms", sp.ElapsedMilliseconds);
				Console.WriteLine("Data directory: {0}", ravenConfiguration.DataDirectory);
            	Console.WriteLine("HostName: {0} Port: {1}, Storage: {2}", ravenConfiguration.HostName ?? "<any>", 
					ravenConfiguration.Port, 
					server.Database.TransactionalStorage.FriendlyName);
            	Console.WriteLine("Server Url: {0}", ravenConfiguration.ServerUrl);
                Console.WriteLine("Press <enter> to stop or 'cls' and <enter> to clear the log");
				if(lauchBrowser)
				{
					try
					{
						Process.Start(ravenConfiguration.ServerUrl);
					}
					catch (Exception e)
					{
						Console.WriteLine("Could not start browser: " + e.Message);
					}
				}
                while (true)
                {
                    var readLine = Console.ReadLine() ?? "";
                    switch (readLine.ToLowerInvariant())
                    {
                        case "cls":
                            Console.Clear();
                            break;
                        case "reset":
                            Console.Clear();
                            return true;
                        default:
                            return false;
                    }
                }
            }
        }

        private static void PrintUsage(OptionSet optionSet)
        {
        	Console.WriteLine(
        		@"
Raven DB
Document Database for the .Net Platform
----------------------------------------
Copyright (C) 2008 - {0} - Hibernating Rhinos
----------------------------------------
Command line ptions:",
        		DateTime.Now.Year);

			optionSet.WriteOptionDescriptions(Console.Out);

        	Console.WriteLine(@"
Enjoy...
");
        }

        private static void EnsureStoppedAndUninstall()
        {
            if (ServiceIsInstalled() == false)
            {
                Console.WriteLine("Service is not installed");
            }
            else
            {
                var stopController = new ServiceController(ProjectInstaller.SERVICE_NAME);

                if (stopController.Status == ServiceControllerStatus.Running)
                    stopController.Stop();

                ManagedInstallerClass.InstallHelper(new[] { "/u", Assembly.GetExecutingAssembly().Location });
            }
        }

        private static void StopService()
        {
            var stopController = new ServiceController(ProjectInstaller.SERVICE_NAME);

            if (stopController.Status == ServiceControllerStatus.Running)
            {
                stopController.Stop();
                stopController.WaitForStatus(ServiceControllerStatus.Stopped);
            }
        }


        private static void StartService()
        {
            var stopController = new ServiceController(ProjectInstaller.SERVICE_NAME);

            if (stopController.Status != ServiceControllerStatus.Running)
            {
                stopController.Start();
                stopController.WaitForStatus(ServiceControllerStatus.Running);
            }
        }

        private static void RestartService()
        {
            var stopController = new ServiceController(ProjectInstaller.SERVICE_NAME);

            if (stopController.Status == ServiceControllerStatus.Running)
            {
                stopController.Stop();
                stopController.WaitForStatus(ServiceControllerStatus.Stopped);
            }
            if (stopController.Status != ServiceControllerStatus.Running)
            {
                stopController.Start();
                stopController.WaitForStatus(ServiceControllerStatus.Running);
            }

        }

        private static void InstallAndStart()
        {
            if (ServiceIsInstalled())
            {
                Console.WriteLine("Service is already installed");
            }
            else
            {
                ManagedInstallerClass.InstallHelper(new[] { Assembly.GetExecutingAssembly().Location });
                var startController = new ServiceController(ProjectInstaller.SERVICE_NAME);
                startController.Start();
            }
        }

        private static bool ServiceIsInstalled()
        {
            return (ServiceController.GetServices().Count(s => s.ServiceName == ProjectInstaller.SERVICE_NAME) > 0);
        }
    }
}
