using System.Collections.Generic;
using Screen_Painter.Models;

namespace ScreenPainter.Tests;

public class FolderEqualityTests
{
    [Fact]
    public void FolderSource_EqualById_CaseInsensitive()
    {
        var a = new FolderSource { Id = "ABC", Name = "One", PathOrUrl = "/x" };
        var b = new FolderSource { Id = "abc", Name = "Two", PathOrUrl = "/y" };

        Assert.Equal(a, b);
    }

    [Fact]
    public void FolderSource_SameId_HasSameHashCode()
    {
        var a = new FolderSource { Id = "id-guid", Name = "One" };
        var b = new FolderSource { Id = "id-guid", Name = "Two" };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void FolderSource_DifferentIds_NotEqual()
    {
        var a = new FolderSource { Id = "id-1" };
        var b = new FolderSource { Id = "id-2" };

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void FolderSource_HashSet_DedupesById()
    {
        var set = new HashSet<FolderSource>
        {
            new() { Id = "dup", Name = "First" },
            new() { Id = "dup", Name = "Second" },
            new() { Id = "unique", Name = "Third" }
        };

        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void CloudAccount_EqualById_CaseInsensitive()
    {
        var a = new CloudAccount { Id = "XYZ", Name = "Acct", ServerUrl = "https://a" };
        var b = new CloudAccount { Id = "xyz", Name = "Other", ServerUrl = "https://b" };

        Assert.Equal(a, b);
    }

    [Fact]
    public void CloudAccount_SameId_HasSameHashCode()
    {
        var a = new CloudAccount { Id = "acct-guid", Name = "Acct" };
        var b = new CloudAccount { Id = "acct-guid", Name = "Other" };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void CloudAccount_DifferentIds_NotEqual()
    {
        var a = new CloudAccount { Id = "1" };
        var b = new CloudAccount { Id = "2" };

        Assert.NotEqual(a, b);
    }
}
