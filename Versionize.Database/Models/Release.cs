namespace Versionize.Database.Models;

public class Release
{
    public string Version { get; set; }
    public List<Commit> Commits { get; set; }
    public DateTime ReleaseDate { get; set; }
}

public class Commit
{
    public string Hash { get; set; }
    public string Message { get; set; }
    public DateTime Date { get; set; }
    public string Author { get; set; }
    public CommitType CommitType { get; set; }
}

public enum CommitType
{
    BreakingChange,
    Bugfix,
    Hotfix,
    Feature,
    Other
}
