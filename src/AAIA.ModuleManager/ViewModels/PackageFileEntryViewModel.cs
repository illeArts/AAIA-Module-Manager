using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.ViewModels;

public sealed class PackageFileEntryViewModel
{
    public string Path         { get; }
    public string SizeLabel    { get; }
    public string Category     { get; }
    public string RiskIcon     { get; }  // ✅ ⚠️ ⛔
    public string CategoryIcon { get; }  // emoji per Kategorie
    public IBrush PathColor    { get; }
    public bool   IsRisk       { get; }  // true wenn Warning oder Blocker

    public PackageFileEntryViewModel(PackageFileEntry entry)
    {
        Path      = entry.Path;
        SizeLabel = entry.SizeLabel;
        Category  = entry.Category;
        IsRisk    = entry.RiskHint != "";

        (RiskIcon, PathColor) = entry.RiskHint switch
        {
            "Blocker" => ("⛔", (IBrush)new SolidColorBrush(Color.Parse("#e05252"))),
            "Warning" => ("⚠️", (IBrush)new SolidColorBrush(Color.Parse("#c9a227"))),
            _         => ("✅", (IBrush)new SolidColorBrush(Color.Parse("#8892a4")))
        };

        CategoryIcon = entry.Category switch
        {
            "Manifest"     => "📋",
            "Assembly"     => "⚙️",
            "Dependency"   => "📦",
            "Documentation"=> "📄",
            "Asset"        => "🖼",
            "Config"       => "🔧",
            "NativeBinary" => "🔩",
            "Suspicious"   => "🚨",
            _              => "📁"
        };
    }

    public static List<PackageFileEntryViewModel> From(PackageInspectionResult result)
        => result.Files.Select(f => new PackageFileEntryViewModel(f)).ToList();
}
