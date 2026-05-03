using McMaster.Extensions.CommandLineUtils;
using Versionize.Changelog;
using Versionize.Changelog.LinkBuilders;
using Versionize.CommandLine;
using Versionize.Config.Validation;
using Versionize.Git;
using Versionize.Lifecycle;
using Versionize.Commands;
using Versionize.ConventionalCommits;
using LibGit2Sharp;
using NuGet.Versioning;
using Versionize.Database.Models;
using Commit = Versionize.Database.Models.Commit;

[Command(Name = "changelog", Description = "Prints a given version's changelog to stdout")]
internal sealed class ChangelogCommand
{
    private readonly IChangelogCmdContextProvider _contextProvider;

    public ChangelogCommand(IChangelogCmdContextProvider contextProvider)
    {
        _contextProvider = contextProvider;
    }

    [SemanticVersion]
    [Option(Description = "The version to include in the changelog")]
    public string? Version { get; }

    [Option(Description = "Text to display before the list of commits")]
    public string? Preamble { get; }

    [Option(Description = "Output type for the changelog (markdown,csv)")]
    public string Format { get; } = "markdown";

    public void OnExecute()
    {
        ChangelogCmdContext context = _contextProvider.GetContext(Version, Preamble);
        ChangelogCmdOptions options = context.Options;
        Repository repo = context.Repository;
        var linkBuilder = LinkBuilderFactory.CreateFor(repo, options.ProjectOptions.Changelog.LinkTemplates);

        CommandLineUI.Verbosity = Versionize.CommandLine.LogLevel.Error;

        SemanticVersion resolvedVersion;
        IReadOnlyList<ConventionalCommit> conventionalCommits;

        // Check if there are any tags present in the repo to start versioning from
        var hasVersionTags = repo.Tags.Any(t => options.ProjectOptions.ExtractTagVersion(t) is not null);

        // If no tags set in the repo create a version of 0.0.1 , possibly the first versioning run
        if (!hasVersionTags && string.IsNullOrEmpty(Version))
        {
            resolvedVersion = new SemanticVersion(0, 0, 1);
            var allCommits = repo.GetCommits(options.ProjectOptions);
            conventionalCommits = ConventionalCommitParser.Parse(allCommits, options.CommitParser);
        }
        else
        {
            // Get the standard commit range starting from the previously set tag
            var (fromRef, toRef) = repo.GetCommitRange(Version, options);
            conventionalCommits = ConventionalCommitProvider.GetCommits(repo, options, fromRef, toRef);
            resolvedVersion = string.IsNullOrEmpty(Version)
                ? repo.Tags
                    .Select(options.ProjectOptions.ExtractTagVersion)
                    .Where(x => x is not null)
                    .OrderDescending()
                    .First()!
                : SemanticVersion.Parse(Version);
        }

        CommandLineUI.Verbosity = Versionize.CommandLine.LogLevel.All;

        if (Format.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            Release release = ChangelogBuilder.GenerateReleaseModel(
                resolvedVersion,
                DateTimeOffset.Now,
                linkBuilder,
                conventionalCommits);

            CommandLineUI.Information(Preamble + ChangelogBuilder.GenerateCSVFromModel(release).TrimEnd());
        }
        else
        {
            string markdown = ChangelogBuilder.GenerateCommitList(
                linkBuilder,
                conventionalCommits,
                options.ProjectOptions.Changelog);

            CommandLineUI.Information(Preamble + markdown.TrimEnd());
        }
    }
}
