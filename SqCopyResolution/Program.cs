using SqCopyResolution.Model;
using SqCopyResolution.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using SqCopyResolution.Model.SonarQube;

namespace SqCopyResolutionr
{
    class Program
    {
        static void Main(string[] args)
        {
            var logger = new ConsoleLogger();
            logger.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "{0} v{1}",
                Assembly.GetEntryAssembly().GetName().Name,
                Assembly.GetEntryAssembly().GetName().Version));
            logger.LogInfo(string.Empty);

            var configParams = new ConfigurationParameters(logger, args);
            logger.LogLevel = configParams.LogLevel;

            if (configParams.Validate())
            {
                switch (configParams.Operation)
                {
                    case Operation.AutoAssign:
                        AutoAssignToAuthors(logger, configParams);
                        break;

                    case Operation.CopyResolution:
                        CopyResolutions(logger, configParams);
                        break;
                }
            }
        }

        private static void CopyResolutions(ConsoleLogger logger, ConfigurationParameters configParams)
        {
            var sqProxy = new SonarQubeProxy(logger, configParams.SonarQubeUrl, configParams.UserName, configParams.Password);

            logger.LogInfo("Getting list of issues for project {0}", configParams.SourceProjectKey);
            var sourceIssues = sqProxy.GetIssuesForProject(configParams.SourceProjectKey, new IssueFilter
            {
                Branch = configParams.SourceBranch,
                Resolutions = new HashSet<string> {"FALSE-POSITIVE", "WONTFIX"}
            });

            if (sourceIssues.Count > 0)
            {
                string noteToAdd;
                if (configParams.AddNote)
                {
                    noteToAdd = string.IsNullOrEmpty(configParams.SourceBranch)
                        ? string.Format(CultureInfo.InvariantCulture, "(copy from {0})", configParams.SourceProjectKey)
                        : string.Format(CultureInfo.InvariantCulture, "(copy from {0}, branch {1})", configParams.SourceProjectKey, configParams.SourceBranch);
                    logger.LogDebug("Will be adding a note '{0}'", noteToAdd);
                }
                else
                {
                    noteToAdd = null;
                }
                foreach (var destinationProjectKey in configParams.DestinationProjectKeys)
                {
                    logger.LogInfo("Copying resolutions to project {0}", destinationProjectKey);
                    var destinationIssues = sqProxy.GetIssuesForProject(destinationProjectKey, new IssueFilter {Branch = configParams.DestinationBranch});
                    foreach (var sourceIssue in sourceIssues)
                    {
                        if ((string.CompareOrdinal(sourceIssue.Resolution, "FALSE-POSITIVE") != 0) || (string.CompareOrdinal(sourceIssue.Resolution, "WONTFIX") != 0))
                        {
                            logger.LogInfo("Issue {0}", sourceIssue);

                            var destinationIssue = destinationIssues.FirstOrDefault(i =>
                                i.Message == sourceIssue.Message
                                && i.Rule == sourceIssue.Rule
                                && i.ComponentPath == sourceIssue.ComponentPath
                                && i.StartLine == sourceIssue.StartLine
                                && i.StartOffset == sourceIssue.StartOffset);

                            if (destinationIssue == null)
                            {
                                logger.LogWarn("\tNot found in the destination project");
                            }
                            else if (!string.IsNullOrEmpty(destinationIssue.Resolution))
                            {
                                logger.LogInfo("\tIssue is already marked as {0} in the destination project.",
                                    destinationIssue.Resolution);
                            }
                            else
                            {
                                logger.LogInfo("\tUpdating issue resolution to {0}",
                                    sourceIssue.Resolution);
                                if (!configParams.DryRun)
                                {
                                    sqProxy.UpdateIssueResolution(destinationIssue.Key, sourceIssue.Resolution, sourceIssue.Comments, noteToAdd);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                logger.LogWarn("There are no issues to copy!");
            }
        }

        private static void AutoAssignToAuthors(ConsoleLogger logger, ConfigurationParameters configParams)
        {
            var sqProxy = new SonarQubeProxy(logger, configParams.SonarQubeUrl, configParams.UserName, configParams.Password);

            logger.LogInfo("Getting list of issues for project {0}", configParams.SourceProjectKey);
            var sourceIssues = sqProxy.GetIssuesForProject(configParams.SourceProjectKey, new IssueFilter
            {
                Branch = configParams.SourceBranch,
                Resolved = false,
                Assigned = false
            });

            if (sourceIssues.Count > 0)
            {
                string noteToAdd;
                if (configParams.AddNote)
                {
                    noteToAdd = string.IsNullOrEmpty(configParams.SourceBranch)
                        ? string.Format(CultureInfo.InvariantCulture, "(copy from {0})", configParams.SourceProjectKey)
                        : string.Format(CultureInfo.InvariantCulture, "(copy from {0}, branch {1})", configParams.SourceProjectKey, configParams.SourceBranch);
                    logger.LogDebug("Will be adding a note '{0}'", noteToAdd);
                }
                else
                {
                    noteToAdd = null;
                }
                foreach (var sourceIssue in sourceIssues)
                {
                    logger.LogInfo("Issue {0}", sourceIssue);

                    string userName;
                    if (!configParams.UserMap.TryGetValue(sourceIssue.Author, out userName))
                    {
                        logger.LogWarn("\tUnable to assign issue authored by unmapped user '{0}'", sourceIssue.Author);
                        continue;
                    }

                    logger.LogInfo("\tAssigning issue to {0}", userName);
                    if (!configParams.DryRun)
                    {
                        sqProxy.AssignIssue(sourceIssue.Key, userName);
                    }
                }
            }
            else
            {
                logger.LogWarn("There are no issues to assign!");
            }
        }
    }
}