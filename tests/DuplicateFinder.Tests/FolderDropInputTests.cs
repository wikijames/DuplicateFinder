namespace DuplicateFinder.Tests;

using Avalonia.Input;
using DuplicateFinder.Desktop;

public sealed class FolderDropInputTests
{
    [Fact]
    public void TextTransfer_IsNotInterpretedAsAFolderDrop()
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText("not a folder transfer"));
        Assert.Throws<ArgumentException>(() => FolderDropInput.GetPaths(transfer));
    }

    [Fact]
    public void EmptyTransfer_DoesNotChangeTheFolderSelection()
    {
        var transfer = new DataTransfer();
        var selection = new ScanFolderSelection();

        Assert.Throws<ArgumentException>(() => selection.Add(FolderDropInput.GetPaths(transfer)));
        Assert.Empty(selection.Folders);
    }
}
