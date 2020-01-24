using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics.CodeAnalysis;
using SqCopyResolution.Services;

namespace SqCopyResolution.Model
{
    public class ConfigurationParameters
    {
        private ILogger Logger { get; set; }

        public Operation Operation { get; set; }

        public string SonarQubeUrl { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string SourceProjectKey { get; set; }
        public string SourceBranch { get; set; }

        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "This is just for passing list of destination project keys to the applicaton.")]
        public string[] DestinationProjectKeys { get; set; }

        public string DestinationBranch { get; set; }
        public Dictionary<string, string> UserMap { get; private set; }
        public LogLevel LogLevel { get; set; }
        public bool AddNote { get; set; }
        public bool DryRun { get; set; }

        public ConfigurationParameters(ILogger logger, string[] commandLineArguments)
        {
            Logger = logger;

            if (Enum.TryParse<Operation>(ConfigurationManager.AppSettings["SQ_Operation"], true, out var parsedOperationFromConfig))
            {
                Operation = parsedOperationFromConfig;
            }
            SonarQubeUrl = ConfigurationManager.AppSettings["SQ_Url"];
            UserName = ConfigurationManager.AppSettings["SQ_UserName"];
            Password = ConfigurationManager.AppSettings["SQ_Password"];
            SourceProjectKey = ConfigurationManager.AppSettings["SQ_SourceProjectKey"];
            SourceBranch = ConfigurationManager.AppSettings["SQ_SourceBranch"];
            DestinationProjectKeys = ConfigurationManager.AppSettings["SQ_DestinationProjectKeys"].Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries);
            DestinationBranch = ConfigurationManager.AppSettings["SQ_DestinationBranch"];
            UserMap = ParseUserMap(ConfigurationManager.AppSettings["SQ_UserMap"]);

            if (string.Equals(ConfigurationManager.AppSettings["SQ_AddNote"], "true", StringComparison.OrdinalIgnoreCase))
            {
                AddNote = true;
            }
            LogLevel logLevel;
            if (Enum.TryParse(ConfigurationManager.AppSettings["SQ_LogLevel"], true, out logLevel))
            {
                LogLevel = logLevel;
            }
            else
            {
                LogLevel = LogLevel.Info;
            }

            if (commandLineArguments != null && commandLineArguments.Length > 0)
            {
                var argsIndex = 0;
                if (!commandLineArguments[argsIndex].StartsWith("-"))
                {
                    var operationStr = commandLineArguments[argsIndex++];
                    if (Enum.TryParse<Operation>(operationStr, true, out var parsedOperation))
                    {
                        Operation = parsedOperation;
                    }
                    else
                    {
                        Logger.LogError("Unknown operation '{0}'", operationStr);
                    }
                }
                while (argsIndex < commandLineArguments.Length)
                {
                    switch (commandLineArguments[argsIndex].ToUpperInvariant())
                    {
                        case "-URL":
                            SonarQubeUrl = commandLineArguments[argsIndex + 1].Trim();
                            argsIndex += 2;
                            break;
                        case "-USERNAME":
                            UserName = commandLineArguments[argsIndex + 1].Trim();
                            argsIndex += 2;
                            break;
                        case "-PASSWORD":
                            Password = commandLineArguments[argsIndex + 1].Trim();
                            argsIndex += 2;
                            break;
                        case "-SOURCEPROJECTKEY":
                            SourceProjectKey = commandLineArguments[argsIndex + 1].Trim();
                            argsIndex += 2;
                            break;
                        case "-SOURCEBRANCH":
                            SourceBranch = commandLineArguments[argsIndex + 1].Trim();
                            argsIndex += 2;
                            break;
                        case "-DESTINATIONPROJECTKEYS":
                            DestinationProjectKeys = commandLineArguments[argsIndex + 1].Trim().Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries);
                            argsIndex += 2;
                            break;
                        case "-DESTINATIONBRANCH":
                            DestinationBranch = commandLineArguments[argsIndex + 1].Trim();
                            argsIndex += 2;
                            break;
                        case "-LOGLEVEL":
                            if (Enum.TryParse<LogLevel>(commandLineArguments[argsIndex + 1], true, out logLevel))
                            {
                                LogLevel = logLevel;
                            }
                            else
                            {
                                Console.WriteLine("Unknown log level: {0}", commandLineArguments[argsIndex + 1]);
                            }
                            argsIndex += 2;
                            break;
                        case "-ADDNOTE":
                            AddNote = true;
                            argsIndex++;
                            break;
                        case "-DRYRUN":
                            DryRun = true;
                            argsIndex++;
                            break;
                        default:
                            Console.WriteLine("Unknown argument: {0}", commandLineArguments[argsIndex]);
                            argsIndex += 2;
                            break;
                    }
                }
            }
        }

        private Dictionary<string, string> ParseUserMap(string str)
        {
            if (String.IsNullOrEmpty(str))
            {
                return new Dictionary<string, string>(0);
            }

            var entries = str.Split(';');
            var result = new Dictionary<string, string>(entries.Length);
            foreach (var entry in entries)
            {
                var parts = entry.Split('=');
                if (parts.Length != 2)
                {
                    Logger.LogError("Invalid user map syntax '{0}'", entry);
                    return null;
                }
                if (result.ContainsKey(parts[0]))
                {
                    Logger.LogError("Duplicate user map entry '{0}'", parts[0]);
                    return null;
                }
                result.Add(parts[0], parts[1]);
            }
            return result;
        }

        internal bool Validate()
        {
            var result = true;

            switch (Operation)
            {
                case Operation.CopyResolution:
                    if (DestinationProjectKeys.Length < 1)
                    {
                        Logger.LogError("List of destination project keys is empty.");
                        result = false;
                    }
                    break;

                case Operation.AutoAssign:
                    if (UserMap != null && UserMap.Count == 0)
                    {
                        Logger.LogError("No user map defined.");
                        result = false;
                    }
                    break;

                default:
                    Logger.LogError("No operation specified");
                    result = false;
                    break;
            }

            if (string.IsNullOrEmpty(SonarQubeUrl))
            {
                Logger.LogError("SonarQube url is empty.");
                result = false;
            }
            if (string.IsNullOrEmpty(UserName))
            {
                Logger.LogError("SonarQube user name is empty.");
                result = false;
            }
            if (string.IsNullOrEmpty(Password))
            {
                Logger.LogError("SonarQube password is empty.");
                result = false;
            }
            if (string.IsNullOrEmpty(SourceProjectKey))
            {
                Logger.LogError("Source project key is empty.");
                result = false;
            }
            if (UserMap == null)
            {
                result = false;
            }

            return result;
        }
    }
}