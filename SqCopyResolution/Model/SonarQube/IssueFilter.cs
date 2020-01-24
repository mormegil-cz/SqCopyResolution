using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Text;

namespace SqCopyResolution.Model.SonarQube
{
    public class IssueFilter
    {
        public string Branch { get; set; }
        public string IssueType { get; set; }
        public bool? Resolved { get; set; }
        public bool? Assigned { get; set; }
        public HashSet<string> Severities { get; set; }
        public HashSet<string> Rules { get; set; }
        public HashSet<string> Assignees { get; set; }
        public HashSet<string> Resolutions { get; set; }

        public string ToQueryString()
        {
            // TODO: URL encoding!
            var result = new StringBuilder();
            if (!string.IsNullOrEmpty(Branch)) result.Append("&branch=").Append(Branch);
            if (!string.IsNullOrEmpty(IssueType)) result.Append("&type=").Append(IssueType);
            if (Resolved != null) result.Append("&resolved=").Append(BoolToStr(Resolved.GetValueOrDefault()));
            if (Assigned != null) result.Append("&assigned=").Append(BoolToStr(Assigned.GetValueOrDefault()));
            if (Severities != null && Severities.Count > 0) result.Append("&severities=").Append(String.Join(",", Severities));
            if (Rules != null && Rules.Count > 0) result.Append("&rules=").Append(String.Join(",", Rules));
            if (Assignees != null && Assignees.Count > 0) result.Append("&assignees=").Append(String.Join(",", Assignees));
            if (Resolutions != null && Resolutions.Count > 0) result.Append("&resolutions=").Append(String.Join(",", Resolutions));
            return result.ToString();
        }

        private static string BoolToStr(bool b) => b ? "true" : "false";
    }
}