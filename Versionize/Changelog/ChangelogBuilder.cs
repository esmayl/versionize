using System.Reflection;
using System.Text;
using Versionize.ConventionalCommits;
using Version = NuGet.Versioning.SemanticVersion;
using Versionize.Config;
using Versionize.Changelog.LinkBuilders;
using Versionize.Database.Models;

namespace Versionize.Changelog;

public sealed class ChangelogBuilder
{
    private ChangelogBuilder(string file)
    {
        FilePath = file;
    }

    public string FilePath { get; }

    public static ChangelogBuilder CreateForPath(string directory)
    {
        var changelogFile = Path.Combine(directory, "CHANGELOG.md");

        return new ChangelogBuilder(changelogFile);
    }

    public void Write(
        Version newVersion,
        Version previousVersion,
        DateTimeOffset versionTime,
        IChangelogLinkBuilder linkBuilder,
        IEnumerable<ConventionalCommit> commits,
        ProjectOptions projectOptions)
    {
        string markdown = GenerateMarkdown(newVersion, previousVersion, versionTime, linkBuilder, commits, projectOptions);

        if (File.Exists(FilePath))
        {
            var contents = File.ReadAllText(FilePath);

            var firstReleaseHeadlineIdx = contents.IndexOf("<a name=\"", StringComparison.Ordinal);

            if (firstReleaseHeadlineIdx >= 0)
            {
                markdown = contents.Insert(firstReleaseHeadlineIdx, markdown);
            }
            else
            {
                markdown = contents + "\n\n" + markdown;
            }

            File.WriteAllText(FilePath, markdown);
        }
        else
        {
            File.WriteAllText(FilePath, projectOptions.Changelog.Header + "\n" + markdown);
        }
    }

    public static string GenerateMarkdown(
        Version newVersion,
        Version previousVersion,
        DateTimeOffset versionTime,
        IChangelogLinkBuilder linkBuilder,
        IEnumerable<ConventionalCommit> commits,
        ProjectOptions projectOptions)
    {
        var currentTag = projectOptions.GetTagName(newVersion);
        var previousTag = projectOptions.GetTagName(previousVersion);
        var compareUrl = linkBuilder.BuildVersionTagLink(currentTag, previousTag);
        var versionTagLink = string.IsNullOrWhiteSpace(compareUrl)
            ? newVersion.ToString()
            : $"[{newVersion}]({compareUrl})";

        var markdown = $"<a name=\"{newVersion}\"></a>";
        markdown += "\n";
        markdown += $"## {versionTagLink} ({versionTime:yyyy-MM-dd})";
        markdown += "\n";
        markdown += "\n";

        return markdown + GenerateCommitList(linkBuilder, commits, projectOptions.Changelog);
    }

    public static string GenerateCSVFromModel(Release model)
    {
        StringBuilder builder = new StringBuilder();


        string headers = string.Join(",",typeof(Release).GetProperties().Where(prop => prop.Name != "Commits").Select(prop => prop.Name));
        builder.Append(headers);
        builder.Append(',');
        
        headers = string.Join(",",typeof(Commit).GetProperties().Select(prop => prop.Name));
        builder.Append(headers);

        builder.Append('\n');
        
        foreach (Commit modelCommit in model.Commits)
        {
            builder.Append(model.Version);
            builder.Append(',');
            builder.Append(model.ReleaseDate);
            builder.Append(',');
            builder.Append(modelCommit.Hash);
            builder.Append(',');
            builder.Append('"');
            builder.Append(modelCommit.Message);
            builder.Append('"');
            builder.Append(',');
            builder.Append(modelCommit.Date);
            builder.Append(',');
            builder.Append(modelCommit.Author);
            builder.Append(',');
            builder.Append(modelCommit.CommitType.ToString());
            
            builder.Append('\n');
        }

        return builder.ToString();
    }
    
    public static Release GenerateReleaseModel(
        Version newVersion,
        Version previousVersion,
        DateTimeOffset versionTime,
        IChangelogLinkBuilder linkBuilder,
        IEnumerable<ConventionalCommit> commits,
        ProjectOptions projectOptions)
    {
        // var currentTag = projectOptions.GetTagName(newVersion);
        // var previousTag = projectOptions.GetTagName(previousVersion);
        // var compareUrl = linkBuilder.BuildVersionTagLink(currentTag, previousTag);
        // var versionTagLink = string.IsNullOrWhiteSpace(compareUrl) ? newVersion.ToString() : $"[{newVersion}]({compareUrl})";

        Release release = new Release();

        release.Version = newVersion.ToFullString();
        release.Commits = GenerateCommitListForCSV(linkBuilder, commits);
        release.ReleaseDate = versionTime.LocalDateTime;
        
        return release;
    }
    
    public static List<Commit> GenerateCommitListForCSV(IChangelogLinkBuilder linkBuilder, IEnumerable<ConventionalCommit> commits)
    {
        List<Commit> markdown = new List<Commit>();
        List<ConventionalCommit> commitsAsList = commits.ToList();

        foreach (var changelogSection in commitsAsList)
        {
            var buildBlock = BuildCommit(changelogSection.Type, linkBuilder, changelogSection);

            markdown.Add(buildBlock);
        }

        return markdown;
    }
    
    private static Commit BuildCommit(string? header, IChangelogLinkBuilder linkBuilder, ConventionalCommit conventionalCommit)
    {
        Commit commit = new Commit();
        
        CommitType commitType = CommitType.Other;

        if (conventionalCommit.IsBreakingChange)
        {
            commitType = CommitType.BreakingChange;
        }
        else if (conventionalCommit.IsFeature)
        {
            commitType = CommitType.Feature;
        }
        else if (conventionalCommit.IsFix)
        {
            commitType = CommitType.Bugfix;
        }
        else
        {
            commitType = CommitType.Other;
        }
        
        commit.CommitType = commitType;
        commit.Hash = conventionalCommit.Sha ?? "000000000000000000000000";
        
        if (conventionalCommit.Notes.Count != 0)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(conventionalCommit.Subject);
            
            foreach (ConventionalCommitNote conventionalCommitNote in conventionalCommit.Notes)
            {
                sb.Append(",");
                sb.Append(conventionalCommitNote.Text?.Replace("\"",string.Empty)); // replace any double quotes with empty string, so the csv can contain the double quotes to prevent corrupt data
            }

            commit.Message = sb.ToString();
        }
        else
        {
            commit.Message = conventionalCommit.Subject != null ? conventionalCommit.Subject.Replace("\"",string.Empty) : "No subject"; // replace any double quotes with empty string, so the csv can contain the double quotes to prevent corrupt data
        }


        return commit;
    }
    

    public static string GenerateCommitList(
        IChangelogLinkBuilder linkBuilder,
        IEnumerable<ConventionalCommit> commits,
        ChangelogOptions changelogOptions)
    {
        var markdown = "";

        var visibleChangelogSections = changelogOptions.Sections is null
            ? []
            : changelogOptions.Sections.Where(x => !x.Hidden);

        foreach (var changelogSection in visibleChangelogSections)
        {
            var matchingCommits = commits.Where(commit => commit.Type == changelogSection.Type);
            var buildBlock = BuildBlock(changelogSection.Section, linkBuilder, matchingCommits);
            if (!string.IsNullOrWhiteSpace(buildBlock))
            {
                markdown += buildBlock;
                markdown += "\n";
            }
        }

        var breaking = BuildBlock("Breaking Changes", linkBuilder, commits.Where(commit => commit.IsBreakingChange));

        if (!string.IsNullOrWhiteSpace(breaking))
        {
            markdown += breaking;
            markdown += "\n";
        }

        if (changelogOptions.IncludeAllCommits.GetValueOrDefault())
        {
            var other = BuildBlock(
                changelogOptions.OtherSection ?? "Other",
                linkBuilder,
                commits.Where(commit => !visibleChangelogSections.Any(x => x.Type == commit.Type) && !commit.IsBreakingChange));

            if (!string.IsNullOrWhiteSpace(other))
            {
                markdown += other;
                markdown += "\n";
            }
        }

        return markdown;
    }

    private static string? BuildBlock(string? header, IChangelogLinkBuilder linkBuilder, IEnumerable<ConventionalCommit> commits)
    {
        if (!commits.Any())
        {
            return null;
        }

        var block = $"### {header}";
        block += "\n";
        block += "\n";

        return commits
            .OrderBy(c => c.Scope)
            .ThenBy(c => c.Subject)
            .Aggregate(block, (current, commit) => current + BuildCommit(commit, linkBuilder) + "\n");
    }

    private static string BuildCommit(ConventionalCommit commit, IChangelogLinkBuilder linkBuilder)
    {
        var sb = new StringBuilder("* ");

        if (!string.IsNullOrWhiteSpace(commit.Scope))
        {
            sb.Append($"**{commit.Scope}:** ");
        }

        var subject = commit.Subject;
        foreach (var issue in commit.Issues)
        {
            if (string.IsNullOrEmpty(subject))
            {
                continue;
            }
            if (string.IsNullOrEmpty(issue.Id))
            {
                continue;
            }
            if (string.IsNullOrEmpty(issue.Token))
            {
                continue;
            }

            var issueLink = linkBuilder.BuildIssueLink(issue.Id);
            if (!string.IsNullOrEmpty(issueLink))
            {
                subject = subject.Replace(issue.Token, $"[{issue.Token}]({issueLink})");
            }
        }

        sb.Append(subject);

        var commitLink = linkBuilder.BuildCommitLink(commit);

        if (!string.IsNullOrWhiteSpace(commitLink))
        {
            var shortSha = commit.Sha?[..7];
            sb.Append($" ([{shortSha}]({commitLink}))");
        }

        return sb.ToString();
    }
}
