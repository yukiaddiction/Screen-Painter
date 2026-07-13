using System.Collections.Generic;
using System.ComponentModel;
using Screen_Painter.Models;

namespace ScreenPainter.Tests;

public class WallpaperCollectionStateTests
{
    [Fact]
    public void NewCollection_ShowsPlaceholder_NotLoading_NoPreview()
    {
        var collection = new WallpaperCollection();

        Assert.False(collection.HasPreview);
        Assert.False(collection.IsPreviewLoading);
        Assert.True(collection.ShowPlaceholder);
    }

    [Fact]
    public void WhileLoading_DoesNotShowPlaceholder()
    {
        var collection = new WallpaperCollection { IsPreviewLoading = true };

        Assert.False(collection.ShowPlaceholder);
        Assert.False(collection.HasPreview);
    }

    [Fact]
    public void WithPreviewImages_HasPreview_NoPlaceholder()
    {
        var collection = new WallpaperCollection
        {
            PreviewImagePaths = new List<string> { "/a.jpg", "/b.jpg" }
        };

        Assert.True(collection.HasPreview);
        Assert.False(collection.ShowPlaceholder);
    }

    [Fact]
    public void PreviewImagePaths_SetNull_CoercesToEmptyList()
    {
        var collection = new WallpaperCollection { PreviewImagePaths = null! };

        Assert.NotNull(collection.PreviewImagePaths);
        Assert.Empty(collection.PreviewImagePaths);
        Assert.False(collection.HasPreview);
    }

    [Fact]
    public void SettingPreviewImagePaths_RaisesDependentPropertyChanges()
    {
        var collection = new WallpaperCollection();
        var changed = new List<string>();
        collection.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        collection.PreviewImagePaths = new List<string> { "/x.jpg" };

        Assert.Contains(nameof(WallpaperCollection.PreviewImagePaths), changed);
        Assert.Contains(nameof(WallpaperCollection.HasPreview), changed);
        Assert.Contains(nameof(WallpaperCollection.ShowPlaceholder), changed);
    }

    [Fact]
    public void SettingIsPreviewLoading_RaisesShowPlaceholderChange()
    {
        var collection = new WallpaperCollection();
        var changed = new List<string>();
        collection.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        collection.IsPreviewLoading = true;

        Assert.Contains(nameof(WallpaperCollection.IsPreviewLoading), changed);
        Assert.Contains(nameof(WallpaperCollection.ShowPlaceholder), changed);
    }

    [Fact]
    public void SettingName_RaisesPropertyChanged()
    {
        var collection = new WallpaperCollection();
        string? changed = null;
        collection.PropertyChanged += (_, e) => changed = e.PropertyName;

        collection.Name = "Vacation";

        Assert.Equal(nameof(WallpaperCollection.Name), changed);
    }
}
