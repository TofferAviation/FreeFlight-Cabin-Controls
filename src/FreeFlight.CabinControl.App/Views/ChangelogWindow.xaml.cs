using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FreeFlight.CabinControl.App.Views;

public partial class ChangelogWindow
{
    public ChangelogWindow(string changelog)
    {
        InitializeComponent();
        RenderChangelog(string.IsNullOrWhiteSpace(changelog)
            ? "No bundled changelog is available."
            : changelog);
    }

    private void RenderChangelog(string changelog)
    {
        var primary = FindResource("TextPrimaryBrush") as Brush ?? Brushes.White;
        var secondary = FindResource("TextSecondaryBrush") as Brush ?? Brushes.LightGray;
        var accent = FindResource("PrimaryBrush") as Brush ?? Brushes.DeepSkyBlue;
        var border = FindResource("BorderSoftBrush") as Brush ?? Brushes.SlateGray;
        var document = new FlowDocument
        {
            PagePadding = new Thickness(4),
            TextAlignment = TextAlignment.Left,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 13,
            Foreground = primary
        };

        foreach (var rawLine in changelog.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line == "---")
            {
                document.Blocks.Add(new BlockUIContainer(new Border
                {
                    Height = 1,
                    Background = border,
                    Margin = new Thickness(0, 12, 0, 12)
                }));
                continue;
            }

            var headingLevel = GetHeadingLevel(line);
            if (headingLevel > 0)
            {
                var heading = StripMarkdown(line[headingLevel..].Trim());
                document.Blocks.Add(new Paragraph(new Run(heading))
                {
                    FontFamily = headingLevel == 1 ? new FontFamily("Georgia") : document.FontFamily,
                    FontSize = headingLevel switch { 1 => 25, 2 => 18, _ => 14 },
                    FontWeight = FontWeights.SemiBold,
                    Foreground = headingLevel == 1 ? primary : accent,
                    Margin = new Thickness(0, headingLevel == 1 ? 4 : 14, 0, 5)
                });
                continue;
            }

            var bullet = Regex.Match(line, @"^(?:[-*•]|\d+\.)\s+(?<text>.+)$");
            if (bullet.Success)
            {
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0, 3, 0, 3),
                    Padding = new Thickness(18, 0, 0, 0),
                    TextIndent = -14,
                    Foreground = primary,
                    LineHeight = 20
                };
                paragraph.Inlines.Add(new Run("• ") { Foreground = accent, FontWeight = FontWeights.Bold });
                paragraph.Inlines.Add(new Run(StripMarkdown(bullet.Groups["text"].Value)));
                document.Blocks.Add(paragraph);
                continue;
            }

            document.Blocks.Add(new Paragraph(new Run(StripMarkdown(line)) { Foreground = secondary })
            {
                Margin = new Thickness(0, 3, 0, 6),
                LineHeight = 20
            });
        }

        ChangelogDocument.Document = document;
    }

    private static int GetHeadingLevel(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] == '#')
        {
            count++;
        }

        return count > 0 && count < line.Length && char.IsWhiteSpace(line[count]) ? count : 0;
    }

    private static string StripMarkdown(string value)
    {
        var clean = Regex.Replace(value, @"\[(?<label>[^\]]+)\]\([^)]+\)", "${label}");
        return clean.Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
