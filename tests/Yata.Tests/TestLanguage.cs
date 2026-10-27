using System.Runtime.CompilerServices;
using Yata.Helpers;

namespace Yata.Tests;

/// <summary>
/// The tests check the Portuguese texts and formats, whatever language the machine running them
/// has (CI runs on an English Windows). StringsTests checks that English has the same texts.
/// </summary>
internal static class TestLanguage
{
    [ModuleInitializer]
    internal static void UsePortuguese() => Lang.Apply("pt-BR");
}
