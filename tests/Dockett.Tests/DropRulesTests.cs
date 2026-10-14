using Dockett.Services;

namespace Dockett.Tests;

public class DropRulesTests
{
    [Theory]
    [InlineData(@"C:\a\file.txt", @"C:\b", false, false, DropAction.Move)]
    [InlineData(@"C:\a\file.txt", @"D:\b", false, false, DropAction.Copy)]
    [InlineData(@"C:\a\file.txt", @"C:\b", true, false, DropAction.Copy)]
    [InlineData(@"C:\a\file.txt", @"D:\b", false, true, DropAction.Move)]
    [InlineData(@"C:\a\file.txt", @"C:\a", false, false, DropAction.None)]
    [InlineData(@"C:\a\file.txt", @"C:\a\", false, true, DropAction.None)]
    [InlineData(@"C:\a\file.txt", @"C:\a", true, false, DropAction.Copy)]
    [InlineData(@"C:\a\folder", @"C:\a\folder", false, false, DropAction.None)]
    [InlineData(@"C:\a\folder", @"C:\a\folder\sub", true, false, DropAction.None)]
    [InlineData(@"C:\a\folder", @"C:\a\folder2", false, false, DropAction.Move)]
    public void Chooses_like_Explorer(string source, string target, bool ctrl, bool shift, DropAction expected)
    {
        Assert.Equal(expected, DropRules.Choose([source], target, ctrl, shift));
    }

    [Fact]
    public void Nothing_to_drop_is_none()
    {
        Assert.Equal(DropAction.None, DropRules.Choose([], @"C:\b", false, false));
    }
}
