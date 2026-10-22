using ATCRoom.Maps;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace ATCRoom.MapCreator
{
    /// <summary>The words of a PDF with their place on the page (for the AIP tables). Kept apart from the WPF files: both have a "Page".</summary>
    internal static class PdfWords
    {
        public static List<PdfWord> Read(string path)
        {
            List<PdfWord> words = [];
            using PdfDocument document = PdfDocument.Open(path);
            foreach (Page page in document.GetPages())
            {
                foreach (Word word in page.GetWords())
                {
                    if (string.IsNullOrWhiteSpace(word.Text)) continue;
                    // Y from the top of the page, as the table is read.
                    words.Add(new PdfWord(page.Number, word.BoundingBox.Left, page.Height - word.BoundingBox.Top, word.Text, word.BoundingBox.Right));
                }
            }
            return words;
        }
    }
}
