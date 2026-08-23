using System.IO;
using VoiceCtrl.Core.Personalization;
using Xunit;

namespace VoiceCtrl.Core.Tests.Personalization;

public class UserFileCacheTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"VoiceCtrlTests_{Path.GetRandomFileName()}");

    private readonly string _path;

    public UserFileCacheTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "dictionary.txt");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void MissingFile_ReturnsTheFallbackWithoutParsing()
    {
        int parseCount = 0;
        var cache = new UserFileCache<string>(_path, CountingParse, "fallback");

        Assert.Equal("fallback", cache.Value);
        Assert.Equal(0, parseCount);

        string CountingParse(string contents)
        {
            parseCount++;
            return contents;
        }
    }

    [Fact]
    public void ExistingFile_IsParsedOnFirstRead()
    {
        File.WriteAllText(_path, "first");
        var cache = new UserFileCache<string>(_path, contents => contents, "fallback");

        Assert.Equal("first", cache.Value);
    }

    [Fact]
    public void UnchangedFile_IsNotReparsed()
    {
        // This runs on every transcription against three files, so the common case has to be a
        // stat and nothing more.
        File.WriteAllText(_path, "first");
        int parseCount = 0;
        var cache = new UserFileCache<string>(_path, CountingParse, "fallback");

        _ = cache.Value;
        _ = cache.Value;
        _ = cache.Value;

        Assert.Equal(1, parseCount);

        string CountingParse(string contents)
        {
            parseCount++;
            return contents;
        }
    }

    [Fact]
    public void EditedFile_IsPickedUpOnTheNextRead()
    {
        File.WriteAllText(_path, "first");
        var cache = new UserFileCache<string>(_path, contents => contents, "fallback");
        Assert.Equal("first", cache.Value);

        File.WriteAllText(_path, "second edit");

        Assert.Equal("second edit", cache.Value);
    }

    [Fact]
    public void DeletedFile_FallsBack()
    {
        File.WriteAllText(_path, "first");
        var cache = new UserFileCache<string>(_path, contents => contents, "fallback");
        Assert.Equal("first", cache.Value);

        File.Delete(_path);

        Assert.Equal("fallback", cache.Value);
    }

    [Fact]
    public void LockedFile_FallsBackRatherThanThrowing()
    {
        // The likeliest cause is the user's editor holding the file open mid-save. Losing the
        // personalisation for one dictation is acceptable; failing the dictation is not.
        File.WriteAllText(_path, "first");

        using var handle = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None);
        var cache = new UserFileCache<string>(_path, contents => contents, "fallback");

        Assert.Equal("fallback", cache.Value);
    }
}
