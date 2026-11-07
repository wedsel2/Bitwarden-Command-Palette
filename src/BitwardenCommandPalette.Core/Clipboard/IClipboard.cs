namespace BitwardenCommandPalette.Core.Clipboard;

/// <summary>
/// The Windows clipboard, reduced to what <see cref="ClipboardService"/> needs. Every method
/// throws <see cref="ClipboardException"/> when the clipboard cannot be used.
/// </summary>
internal interface IClipboard
{
    /// <summary>
    /// Replaces the clipboard content with <paramref name="text"/>, marked so Windows keeps it
    /// out of clipboard history and cloud clipboard and clipboard managers skip it (ADR 11).
    /// If the marks cannot all be set, the clipboard is left empty.
    /// </summary>
    /// <param name="text">The text to copy.</param>
    void SetText(string text);

    /// <summary>
    /// Empties the clipboard if its text is exactly <paramref name="text"/>. The check and the
    /// clear happen while the clipboard is open, so a copy made in between cannot be wiped.
    /// </summary>
    /// <param name="text">The text to compare with, ordinally.</param>
    /// <returns>True if the clipboard was emptied.</returns>
    bool ClearIfHolds(string text);
}
