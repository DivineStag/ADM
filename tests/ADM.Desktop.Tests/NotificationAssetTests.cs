using System.IO;
using System.Security.Cryptography;
using ADM.Desktop.Presentation;
using ADM.Desktop.Services;
using ADM.Domain;

namespace ADM.Desktop.Tests;

public sealed class NotificationAssetTests
{
    [Fact]
    public void Outgoing_pop_is_packaged_unchanged()
    {
        string path = NotificationPlayer.OutgoingAssetPath(AppContext.BaseDirectory);
        Assert.True(File.Exists(path));
        Assert.Equal("BB343A49535045EE3350F30FE01848CE8F933B1394710A71069C96435116AF54", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }
    [Fact]
    public void System_lines_never_notify_even_if_their_key_matches_an_important_dispatcher_event()
    {
        var message = new ConversationMessage(Guid.NewGuid(), Guid.NewGuid(), "boarding.approval:1", "Sarah", "Ready to board?", DateTimeOffset.UtcNow);
        Assert.True(ConversationPresentation.ShouldNotify(message));
        Assert.False(ConversationPresentation.ShouldNotify(message with { IsSystem = true }));
        Assert.False(ConversationPresentation.ShouldNotify(message with { SemanticKey = "system.service:Water:started", IsSystem = true }));
    }
    [Fact]
    public void Packaged_notification_uses_the_supplied_MP3_when_present_or_the_original_generated_tone()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "incoming.mp3");
        if (!File.Exists(path))
        {
            Assert.EndsWith("incoming.wav", NotificationPlayer.AssetPath(AppContext.BaseDirectory));
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(NotificationPlayer.AssetPath(AppContext.BaseDirectory)), 0, 4));
            return;
        }
        Assert.Equal(path, NotificationPlayer.AssetPath(AppContext.BaseDirectory));
        Assert.Equal("D03BBDF4F72384121A72FAFBEB4C19D84E018DF8528E819F608F88F203AB76D1", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }
    [Fact]
    public void Source_checkout_without_the_unverified_MP3_uses_the_original_tone_and_preserves_MP3_preference_when_present()
    {
        string root = Path.Combine(Path.GetTempPath(), "adm-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        try
        {
            Assert.Equal(Path.Combine(root, "Assets", "incoming.wav"), NotificationPlayer.AssetPath(root));
            File.WriteAllBytes(Path.Combine(root, "Assets", "incoming.mp3"), [1]);
            Assert.Equal(Path.Combine(root, "Assets", "incoming.mp3"), NotificationPlayer.AssetPath(root));
        }
        finally { Directory.Delete(root, true); }
    }

}
