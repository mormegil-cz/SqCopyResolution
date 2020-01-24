using Newtonsoft.Json;
using SqCopyResolution.Model.SonarQube;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using Microsoft.SqlServer.Server;

namespace SqCopyResolution.Services
{
    public class SonarQubeProxy
    {
        private ILogger Logger { get; set; }
        private string SonarQubeUrl { get; set; }
        private string UserName { get; set; }
        private string Password { get; set; }

        public SonarQubeProxy(ILogger logger, string sonarQubeUrl, string userName, string password)
        {
            Logger = logger;
            SonarQubeUrl = sonarQubeUrl;
            UserName = userName;
            Password = password;
        }

        public IList<Issue> GetIssuesForProject(string projectKey, IssueFilter filter)
        {
            var result = new List<Issue>();

            Logger.LogDebug("Getting list of issues for project {0} (branch {1})", projectKey, filter.Branch);

            // SonarQube cannot return more than 10000 issues in one response.
            // Let's try to find out, what the number of issues is
            var numberOfIssues = GetNumberOfIssuesForProject(projectKey, filter);

            if (numberOfIssues > 0)
            {
                if (numberOfIssues < 10000)
                {
                    const int pageSize = 500;
                    var pageIndex = 1;
                    do
                    {
                        var uri = new Uri(string.Format(CultureInfo.InvariantCulture,
                            "{0}/api/issues/search?projectKeys={1}&additionalFields=comments&p={2}&ps={3}{4}",
                            SonarQubeUrl,
                            projectKey,
                            pageIndex,
                            pageSize,
                            filter.ToQueryString()));

                        var responseContent = GetFromServer(uri);
                        if (!string.IsNullOrEmpty(responseContent))
                        {
                            ApiIssuesSearchResult apiResult = JsonConvert.DeserializeObject<ApiIssuesSearchResult>(responseContent);
                            result.AddRange(apiResult.Issues);
                            if (apiResult.Paging.Total < (apiResult.Paging.PageIndex * apiResult.Paging.PageSize))
                            {
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }

                        pageIndex++;
                    } while (true);
                }
                else
                {
                    // If the number of issues is too high, we need to get their list by components
                    var components = GetProjectComponents(projectKey, filter.Branch);
                    if (components != null)
                    {
                        foreach (var component in components)
                        {
                            result.AddRange(GetIssuesForComponent(component, filter));
                        }
                    }
                }
            }

            Logger.LogDebug("\t{0} issues found", result.Count);

            return result;
        }

        private int GetNumberOfIssuesForProject(string projectKey, IssueFilter filter)
        {
            Logger.LogDebug("Getting number of issues for project {0} (branch '{1}')", projectKey, filter.Branch);

            var uri = new Uri(string.Format(CultureInfo.InvariantCulture,
                "{0}/api/issues/search?projectKeys={1}&p=1&ps=1{2}",
                SonarQubeUrl,
                projectKey,
                filter.ToQueryString()));

            var responseContent = GetFromServer(uri);
            if (!string.IsNullOrEmpty(responseContent))
            {
                ApiIssuesSearchResult apiResult = JsonConvert.DeserializeObject<ApiIssuesSearchResult>(responseContent);
                return apiResult.Paging.Total;
            }

            return -1;
        }

        private IList<Issue> GetIssuesForComponent(Component component, IssueFilter filter)
        {
            Logger.LogDebug("Getting list of issues for component {0} (branch {1})", component.Key, filter.Branch);

            var result = new List<Issue>();

            const int pageSize = 500;
            var pageIndex = 1;
            do
            {
                var uri = new Uri(string.Format(CultureInfo.InvariantCulture,
                    "{0}/api/issues/search?componentKeys={1}&p={2}&ps={3}{4}",
                    SonarQubeUrl,
                    component.Key,
                    pageIndex,
                    pageSize,
                    filter.ToQueryString()));

                var responseContent = GetFromServer(uri);
                if (!string.IsNullOrEmpty(responseContent))
                {
                    ApiIssuesSearchResult apiResult = JsonConvert.DeserializeObject<ApiIssuesSearchResult>(responseContent);
                    result.AddRange(apiResult.Issues);
                    if (apiResult.Paging.Total < (apiResult.Paging.PageIndex * apiResult.Paging.PageSize))
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }

                pageIndex++;
            } while (true);

            Logger.LogDebug("\t{0} issues found", result.Count);

            return result;
        }

        public void UpdateIssueResolution(string issueKey, string newResolution, Comment[] comments, string noteToAdd)
        {
            if (newResolution == null)
            {
                throw new ArgumentNullException(nameof(newResolution));
            }

            Logger.LogDebug("Updating resolution for issue {0} to {1}", issueKey, newResolution);

            string transition = string.Empty;

            switch (newResolution.ToUpperInvariant())
            {
                case "FALSE-POSITIVE":
                    transition = "falsepositive";
                    break;
                case "WONTFIX":
                    transition = "wontfix";
                    break;
                default:
                    throw new InvalidOperationException("Cannot update issue resolution to value " + newResolution);
            }

            var uri = new Uri(string.Format(CultureInfo.InvariantCulture,
                "{0}/api/issues/do_transition",
                SonarQubeUrl));
            PostToServer(uri, new[]
            {
                new KeyValuePair<string, string>("issue", issueKey),
                new KeyValuePair<string, string>("transition", transition)
            });

            if (comments != null)
            {
                foreach (var comment in comments)
                {
                    PostComment(issueKey, noteToAdd == null ? comment.HtmlText : comment.HtmlText + " " + noteToAdd);
                }
            }
            if (comments == null && noteToAdd != null)
            {
                PostComment(issueKey, noteToAdd);
            }
        }

        public void AssignIssue(string issueKey, string assignee)
        {
            if (assignee == null)
            {
                throw new ArgumentNullException(nameof(assignee));
            }

            Logger.LogDebug("Assigning issue {0} to {1}", issueKey, assignee);

            var uri = new Uri(string.Format(CultureInfo.InvariantCulture,
                "{0}/api/issues/assign",
                SonarQubeUrl));
            PostToServer(uri, new[]
            {
                new KeyValuePair<string, string>("issue", issueKey),
                new KeyValuePair<string, string>("assignee", assignee)
            });
        }

        private void PostComment(string issueKey, string commentHtmlText)
        {
            var uri = new Uri(string.Format(CultureInfo.InvariantCulture,
                "{0}/api/issues/add_comment",
                SonarQubeUrl));
            PostToServer(uri, new[]
            {
                new KeyValuePair<string, string>("issue", issueKey),
                new KeyValuePair<string, string>("text", commentHtmlText)
            });
        }

        private IList<Component> GetProjectComponents(string projectKey, string branchName)
        {
            Logger.LogDebug("Getting list of components for project {0} (branch {1})", projectKey, branchName);

            var components = new List<Component>();

            const int pageSize = 500;
            var pageIndex = 1;
            do
            {
                var uri = new Uri(string.Format(CultureInfo.InvariantCulture,
                    "{0}/api/components/tree?baseComponentKey={1}&qualifiers=DIR&p={2}&ps={3}{4}",
                    SonarQubeUrl,
                    projectKey,
                    pageIndex,
                    pageSize,
                    !string.IsNullOrEmpty(branchName) ? "&branch=" + branchName : string.Empty));

                var responseContent = GetFromServer(uri);
                if (!string.IsNullOrEmpty(responseContent))
                {
                    ApiComponentsTreeResult apiResult = JsonConvert.DeserializeObject<ApiComponentsTreeResult>(responseContent);
                    components.AddRange(apiResult.Components);
                    if (apiResult.Paging.Total < (apiResult.Paging.PageIndex * apiResult.Paging.PageSize))
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }

                pageIndex++;
            } while (true);

            Logger.LogDebug("\t{0} components found", components.Count);

            return components;
        }

        private string GetFromServer(Uri uri)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    AddHttpAuthorization(httpClient);
                    var response = httpClient.GetAsync(uri).Result;
                    var responseContent = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        return responseContent;
                    }
                    else
                    {
                        Logger.LogError("Cannot get result from server! Uri = {0}, Status code = {1}, Response content: {2}",
                            uri,
                            response.StatusCode,
                            responseContent);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Cannot get result from server! Uri = {0}, Exception: {1}", uri, ex.ToString());
                throw;
            }

            return string.Empty;
        }


        private void PostToServer(Uri uri, KeyValuePair<string, string>[] parameters)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    AddHttpAuthorization(httpClient);

                    using (var content = new FormUrlEncodedContent(parameters))
                    {
                        var response = httpClient.PostAsync(uri, content).Result;
                        var responseContent = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode != System.Net.HttpStatusCode.OK)
                        {
                            Logger.LogError("Error when posting to the server! Uri = {0}, Status code = {1}, Response content: {2}",
                                uri,
                                response.StatusCode,
                                responseContent);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Cannot post to server! Uri: {0}, Exception: {0}", uri, ex.ToString());
                throw;
            }
        }

        private void AddHttpAuthorization(HttpClient httpClient)
        {
            var byteArray = Encoding.ASCII.GetBytes(UserName + ":" + Password);
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        }
    }
}