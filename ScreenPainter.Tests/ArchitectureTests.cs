using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Screen_Painter.Models;
using Screen_Painter.Services.Scheduling;

namespace ScreenPainter.Tests;

/// <summary>
/// Architecture invariants enforced by reflection over the pure domain + logic layers that
/// are compile-linked into this test assembly. These guard persistence compatibility and
/// keep the extracted logic classes free of platform dependencies.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(WallpaperCollection).Assembly;

    [Fact]
    public void TriggerType_HasExactExpectedMembers()
    {
        // Enum values are persisted as integers in collections.json — reordering or removing
        // members would silently corrupt saved data.
        Assert.Equal(new[] { "Timer", "ScreenAwake" }, Enum.GetNames(typeof(TriggerType)));
        Assert.Equal(0, (int)TriggerType.Timer);
        Assert.Equal(1, (int)TriggerType.ScreenAwake);
    }

    [Fact]
    public void TargetScreen_HasExactExpectedMembers()
    {
        Assert.Equal(new[] { "Home", "Lock", "Both" }, Enum.GetNames(typeof(TargetScreen)));
        Assert.Equal(0, (int)TargetScreen.Home);
        Assert.Equal(1, (int)TargetScreen.Lock);
        Assert.Equal(2, (int)TargetScreen.Both);
    }

    [Fact]
    public void StorageType_HasExactExpectedMembers()
    {
        Assert.Equal(new[] { "Local", "WebDav", "OAuthCloud" }, Enum.GetNames(typeof(StorageType)));
        Assert.Equal(0, (int)StorageType.Local);
        Assert.Equal(1, (int)StorageType.WebDav);
        Assert.Equal(2, (int)StorageType.OAuthCloud);
    }

    [Fact]
    public void ModelTypes_LiveInModelsNamespace()
    {
        var modelTypes = new[]
        {
            typeof(WallpaperCollection), typeof(FolderSource), typeof(CloudAccount),
            typeof(CredentialedEntity), typeof(TriggerType), typeof(TargetScreen), typeof(StorageType)
        };

        Assert.All(modelTypes, t => Assert.Equal("Screen_Painter.Models", t.Namespace));
    }

    [Fact]
    public void WallpaperCollection_PersistedProperties_HaveJsonPropertyName()
    {
        // Any public read/write property that is NOT [JsonIgnore] is persisted and must carry
        // an explicit [JsonPropertyName] so the on-disk contract is stable.
        var persisted = typeof(WallpaperCollection)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null);

        Assert.All(persisted, p =>
            Assert.True(
                p.GetCustomAttribute<JsonPropertyNameAttribute>() != null,
                $"Persisted property '{p.Name}' is missing [JsonPropertyName]."));
    }

    [Fact]
    public void WallpaperCollection_ViewStateProperties_AreJsonIgnored()
    {
        var viewState = new[]
        {
            nameof(WallpaperCollection.PreviewImagePaths),
            nameof(WallpaperCollection.PreviewImagePath),
            nameof(WallpaperCollection.PreviewImageSource),
            nameof(WallpaperCollection.IsPreviewLoading),
            nameof(WallpaperCollection.HasPreview),
            nameof(WallpaperCollection.ShowPlaceholder)
        };

        foreach (var name in viewState)
        {
            var prop = typeof(WallpaperCollection).GetProperty(name);
            Assert.NotNull(prop);
            Assert.NotNull(prop!.GetCustomAttribute<JsonIgnoreAttribute>());
            Assert.Null(prop.GetCustomAttribute<JsonPropertyNameAttribute>());
        }
    }

    [Fact]
    public void ExtractedLogic_HasNoMauiOrAndroidDependencies()
    {
        // RotationGate and SchedulePolicy must stay pure so they remain unit-testable off-device.
        var logicTypes = new[] { typeof(RotationGate), typeof(SchedulePolicy) };

        foreach (var type in logicTypes)
        {
            var referenced = type.Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);
            Assert.DoesNotContain(referenced, n =>
                n.StartsWith("Microsoft.Maui", StringComparison.OrdinalIgnoreCase) ||
                n.StartsWith("Mono.Android", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("Xamarin.AndroidX", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void WallpaperCollection_ImplementsINotifyPropertyChanged()
    {
        Assert.True(typeof(System.ComponentModel.INotifyPropertyChanged)
            .IsAssignableFrom(typeof(WallpaperCollection)));
    }
}
