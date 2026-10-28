using System.IO;
using Yata.Services;

namespace Yata.Tests;

/// <summary>A shelved path is only dropped when it's really gone, not when its drive is away.</summary>
public sealed class ShelfTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "yata-tests", Guid.NewGuid().ToString("N"));

    public ShelfTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_file_that_is_there_is_present()
    {
        string file = Path.Combine(_dir, "proposta.pdf");
        File.WriteAllText(file, "x");
        Assert.Equal(ShelfState.Present, ShelfItem.Check(file));
        Assert.Equal(ShelfState.Present, ShelfItem.Check(_dir));
    }

    [Fact]
    public void Deleted_or_moved_on_a_drive_that_answers_is_gone()
    {
        Assert.Equal(ShelfState.Gone, ShelfItem.Check(Path.Combine(_dir, "apagado.pdf")));
        Assert.Equal(ShelfState.Gone, ShelfItem.Check(Path.Combine(_dir, "pasta-movida", "a.pdf")));
    }

    [Fact]
    public void On_a_drive_that_isnt_there_it_is_unavailable()
    {
        char? free = null;
        for (char letter = 'Z'; letter >= 'D' && free == null; letter--)
        {
            if (!Directory.Exists($"{letter}:\\"))
                free = letter;
        }
        Assert.NotNull(free);
        Assert.Equal(ShelfState.Unavailable, ShelfItem.Check($"{free}:\\fotos\\viagem.jpg"));
    }
}
