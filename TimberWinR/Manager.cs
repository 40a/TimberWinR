﻿using System.IO;
using System.Net.Sockets;
using System.Reflection;
using Newtonsoft.Json;
using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TimberWinR.Inputs;
using TimberWinR.Outputs;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace TimberWinR
{
    /// <summary>
    /// The Manager class for TimberWinR
    /// </summary>
    public class Manager
    {
        public Configuration Config { get; set; }
        public List<OutputSender> Outputs { get; set; }
        public List<TcpInputListener> Tcps { get; set; }
        public List<TcpInputListener> Udps { get; set; }
        public List<InputListener> Listeners { get; set; }
        public DateTime StartedOn { get; set; }
        public string JsonConfig { get; set; }
        public string LogfileDir { get; set; }

        public int NumConnections
        {
            get { return numConnections; }
        }

        public int NumMessages
        {
            get { return numMessages; }
        }

        private static int numConnections;
        private static int numMessages;


        public void Shutdown()
        {
            LogManager.GetCurrentClassLogger().Info("Shutting Down");

            foreach (InputListener listener in Listeners)
                listener.Shutdown();

            LogManager.GetCurrentClassLogger().Info("Completed ShutDown");
        }


        public void IncrementMessageCount(int count = 1)
        {
            Interlocked.Add(ref numMessages, count);
        }

        public Manager(string jsonConfigFile, string logLevel, string logfileDir, CancellationToken cancelToken)
        {
            LogsFileDatabase.Manager = this;           
  
            StartedOn = DateTime.UtcNow;

            var vfi = new FileInfo(jsonConfigFile);

            JsonConfig = vfi.FullName;
            LogfileDir = logfileDir;


            numMessages = 0;
            numConnections = 0;

            Outputs = new List<OutputSender>();
            Listeners = new List<InputListener>();

            var loggingConfiguration = new LoggingConfiguration();

            // Create our default targets
            var coloredConsoleTarget = new ColoredConsoleTarget();

            Target fileTarget = CreateDefaultFileTarget(logfileDir);

            loggingConfiguration.AddTarget("Console", coloredConsoleTarget);
            loggingConfiguration.AddTarget("DailyFile", fileTarget);

            // The LogLevel.Trace means has to be at least Trace to show up on console
            loggingConfiguration.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, coloredConsoleTarget));
            // LogLevel.Debug means has to be at least Debug to show up in logfile
            loggingConfiguration.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, fileTarget));

            LogManager.Configuration = loggingConfiguration;
            LogManager.EnableLogging();

            LogManager.GlobalThreshold = LogLevel.FromString(logLevel);

            LogManager.GetCurrentClassLogger()
                .Info("TimberWinR Version {0}", GetAssemblyByName("TimberWinR.ServiceHost").GetName().Version.ToString());


            LogManager.GetCurrentClassLogger()
                .Info("Database Directory: {0}", LogsFileDatabase.Instance.DatabaseFileName);


            try
            {
                // Is it a directory?
                if (Directory.Exists(jsonConfigFile))
                {
                    DirectoryInfo di = new DirectoryInfo(jsonConfigFile);
                    LogManager.GetCurrentClassLogger().Info("Initialized, Reading Configurations From {0}", di.FullName);
                    Config = Configuration.FromDirectory(jsonConfigFile);
                }
                else
                {
                    var fi = new FileInfo(jsonConfigFile);

                    LogManager.GetCurrentClassLogger().Info("Initialized, Reading Configurations From File: {0}", fi.FullName);

                    if (!fi.Exists)
                        throw new FileNotFoundException("Missing config file", jsonConfigFile);

                    LogManager.GetCurrentClassLogger().Info("Initialized, Reading Config: {0}", fi.FullName);
                    Config = Configuration.FromFile(jsonConfigFile);
                }
            }
            catch (JsonSerializationException jse)
            {
                LogManager.GetCurrentClassLogger().Error(jse);
            }
            catch (Exception ex)
            {
                LogManager.GetCurrentClassLogger().Error(ex);               
            }
            LogManager.GetCurrentClassLogger().Info("Log Directory {0}", logfileDir);
            LogManager.GetCurrentClassLogger().Info("Logging Level: {0}", LogManager.GlobalThreshold);

            // Read the Configuration file
            if (Config != null)
            {
                if (Config.RedisOutputs != null)
                {
                    foreach (var ro in Config.RedisOutputs)
                    {
                        var redis = new RedisOutput(this, ro, cancelToken);
                        Outputs.Add(redis);
                    }

                }
                if (Config.ElasticsearchOutputs != null)
                {
                    foreach (var ro in Config.ElasticsearchOutputs)
                    {
                        var els = new ElasticsearchOutput(this, ro, cancelToken);
                        Outputs.Add(els);
                    }
                }
                if (Config.StdoutOutputs != null)
                {
                    foreach (var ro in Config.StdoutOutputs)
                    {
                        var stdout = new StdoutOutput(this, ro, cancelToken);
                        Outputs.Add(stdout);
                    }
                }

                foreach (Parser.IISW3CLog iisw3cConfig in Config.IISW3C)
                {
                    var elistner = new IISW3CInputListener(iisw3cConfig, cancelToken);
                    Listeners.Add(elistner);
                    foreach (var output in Outputs)
                        output.Connect(elistner);
                }

                foreach (Parser.W3CLog iisw3cConfig in Config.W3C)
                {
                    var elistner = new W3CInputListener(iisw3cConfig, cancelToken);
                    Listeners.Add(elistner);
                    foreach (var output in Outputs)
                        output.Connect(elistner);
                }

                foreach (Parser.WindowsEvent eventConfig in Config.Events)
                {
                    var elistner = new WindowsEvtInputListener(eventConfig, cancelToken);
                    Listeners.Add(elistner);
                    foreach (var output in Outputs)
                        output.Connect(elistner);
                }

                foreach (var logConfig in Config.Logs)
                {
                    var elistner = new LogsListener(logConfig, cancelToken);
                    Listeners.Add(elistner);
                    foreach (var output in Outputs)
                        output.Connect(elistner);
                }

                foreach (var tcp in Config.Tcps)
                {
                    var elistner = new TcpInputListener(cancelToken, tcp.Port);
                    Listeners.Add(elistner);
                    foreach (var output in Outputs)
                        output.Connect(elistner);
                }

                foreach (var udp in Config.Udps)
                {
                    var elistner = new UdpInputListener(cancelToken, udp.Port);
                    Listeners.Add(elistner);
                    foreach (var output in Outputs)
                        output.Connect(elistner);
                }

                foreach (var stdin in Config.Stdins)
                {
                    var elistner = new StdinListener(stdin, cancelToken);
                    Listeners.Add(elistner);
                    foreach (var output in Outputs)
                        output.Connect(elistner);
                }

                var computerName = System.Environment.MachineName + "." +
                       Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                           @"SYSTEM\CurrentControlSet\services\Tcpip\Parameters")
                           .GetValue("Domain", "")
                           .ToString();

                foreach (var output in Outputs)
                {
                    var name = Assembly.GetExecutingAssembly().GetName();
                    JObject json = new JObject(
                     new JProperty("TimberWinR",
                         new JObject(
                             new JProperty("version", GetAssemblyByName("TimberWinR.ServiceHost").GetName().Version.ToString()),
                             new JProperty("host", computerName),
                             new JProperty("output", output.Name),
                             new JProperty("initialized", DateTime.UtcNow)
                             )));
                    json.Add(new JProperty("type", "Win32-TimberWinR"));
                    json.Add(new JProperty("host", computerName));
                    output.Startup(json);
                }
            }

        }


        private Assembly GetAssemblyByName(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().
                   SingleOrDefault(assembly => assembly.GetName().Name == name);
        }


        /// <summary>
        /// Creates the default <see cref="FileTarget"/>.
        /// </summary>
        /// <param name="logPath"></param>
        /// <returns>
        /// The NLog file target used in the default logging configuration.
        /// </returns>
        public static FileTarget CreateDefaultFileTarget(string logPath)
        {
            return new FileTarget
            {
                ArchiveEvery = FileArchivePeriod.None,
                ArchiveAboveSize = 5 * 1024 * 1024,
                MaxArchiveFiles = 5,
                BufferSize = 10,
                FileName = Path.Combine(logPath, "TimberWinR", "TimberWinR.log"),
                ArchiveFileName = Path.Combine(logPath, "TimberWinR_log-{#######}.log"),
            };
        }

    }
}
