using System.IO;
using VoiceCtrl.Core.Config;
using Xunit;

namespace VoiceCtrl.Core.Tests.Config;

public sealed class UserDataRemovalTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);

    private readonly string _parent = Path.Combine(Path.GetTempPath(), $"voicectrl-test-removal-{Guid.NewGuid()}");

    // Named like the real data root, since the removal refuses any other folder name.
    private string Root => Path.Combine(_parent, "VoiceCtrl");

    public UserDataRemovalTests() => Directory.CreateDirectory(Root);

    [Fact]
    public void DeletesTheWholeRoot_NestedFoldersAndReadOnlyFilesToo()
    {
        File.WriteAllText(Path.Combine(Root, "settings.json"), "{}");
        Directory.CreateDirectory(Path.Combine(Root, "models", "european"));
        File.WriteAllText(Path.Combine(Root, "models", "european", "model.onnx"), "weights");
        string readOnly = Path.Combine(Root, "webview2", "Local State");
        Directory.CreateDirectory(Path.GetDirectoryName(readOnly)!);
        File.WriteAllText(readOnly, "state");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);

        bool removed = UserDataRemoval.DeleteAll(Root, Patience, _ => { });

        Assert.True(removed);
        Assert.False(Directory.Exists(Root));
        Assert.True(Directory.Exists(_parent));
    }

    [Fact]
    public void AMissingRoot_CountsAsRemoved()
    {
        Directory.Delete(Root);

        Assert.True(UserDataRemoval.DeleteAll(Root, Patience, _ => { }));
    }

    [Fact]
    public void RefusesAFolderNotNamedLikeTheDataRoot()
    {
        string other = Path.Combine(_parent, "Documents");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "keep.txt"), "mine");

        bool removed = UserDataRemoval.DeleteAll(other, Patience, _ => { });

        Assert.False(removed);
        Assert.True(File.Exists(Path.Combine(other, "keep.txt")));
    }

    [Fact]
    public void RefusesADriveRoot()
    {
        string driveRoot = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.False(UserDataRemoval.DeleteAll(driveRoot, Patience, _ => { }));
    }

    [Fact]
    public void AFileLockedForAMoment_IsDeletedOnARetry()
    {
        string cache = Path.Combine(Root, "webview2", "cache.db");
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        var locked = new FileStream(cache, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        int waits = 0;

        // The first wait is where a WebView2 process that outlived the app lets go of its files.
        bool removed = UserDataRemoval.DeleteAll(Root, Patience, _ =>
        {
            waits++;
            locked.Dispose();
        });

        Assert.True(removed);
        Assert.Equal(1, waits);
        Assert.False(Directory.Exists(Root));
    }

    [Fact]
    public void AFileLockedThroughout_GivesUpAfterThePatience_AndStillDeletesTheRest()
    {
        File.WriteAllText(Path.Combine(Root, "history.json"), "[]");
        string cache = Path.Combine(Root, "webview2", "cache.db");
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        using var locked = new FileStream(cache, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        TimeSpan waited = TimeSpan.Zero;

        bool removed = UserDataRemoval.DeleteAll(Root, Patience, pause => waited += pause);

        Assert.False(removed);
        Assert.True(waited >= Patience);
        Assert.False(File.Exists(Path.Combine(Root, "history.json")));
        Assert.True(File.Exists(cache));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_parent, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
