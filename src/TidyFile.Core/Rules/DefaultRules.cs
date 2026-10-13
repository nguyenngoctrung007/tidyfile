namespace TidyFile.Core.Rules;

/// <summary>Starter rules written to rules.json on first run. Specific rules come first.</summary>
public static class DefaultRules
{
    public static RulesDocument Create() => new()
    {
        Rules = new List<Rule>
        {
            new()
            {
                Name = "Invoices & receipts",
                Extensions = { "pdf" },
                NamePattern = @"invoice|receipt|bill|statement|payslip",
                Destination = @"{Documents}\Finance",
            },
            new()
            {
                Name = "Screenshots",
                Extensions = { "png", "jpg", "jpeg" },
                NamePattern = @"^(screenshot|screen shot|capture|snip)",
                Destination = @"{Pictures}\Screenshots",
            },
            new()
            {
                Name = "Installers",
                Extensions = { "exe", "msi", "msix", "msixbundle", "appx", "appinstaller" },
                Destination = @"{Downloads}\Installers",
            },
            new()
            {
                Name = "Archives",
                Extensions = { "zip", "rar", "7z", "tar", "gz", "tgz" },
                Destination = @"{Downloads}\Archives",
            },
            new()
            {
                Name = "Documents",
                Extensions = { "pdf", "doc", "docx", "odt", "rtf", "txt", "md" },
                Destination = @"{Documents}\Docs",
            },
            new()
            {
                Name = "Spreadsheets",
                Extensions = { "xls", "xlsx", "xlsm", "ods", "csv" },
                Destination = @"{Documents}\Spreadsheets",
            },
            new()
            {
                Name = "Presentations",
                Extensions = { "ppt", "pptx", "odp", "key" },
                Destination = @"{Documents}\Presentations",
            },
            new()
            {
                Name = "Images",
                Extensions = { "jpg", "jpeg", "png", "gif", "webp", "heic", "bmp", "svg", "tif", "tiff" },
                Destination = @"{Pictures}",
            },
            new()
            {
                Name = "Videos",
                Extensions = { "mp4", "mov", "mkv", "avi", "webm", "wmv" },
                Destination = @"{Videos}",
            },
            new()
            {
                Name = "Audio",
                Extensions = { "mp3", "wav", "flac", "m4a", "aac", "ogg" },
                Destination = @"{Music}",
            },
            new()
            {
                Name = "Code & data",
                Extensions = { "json", "xml", "yaml", "yml", "sql", "ps1", "py", "cs", "js", "ts" },
                Destination = @"{Documents}\Dev",
            },
            new()
            {
                Name = "Fonts",
                Extensions = { "ttf", "otf", "woff", "woff2" },
                Destination = @"{Downloads}\Fonts",
            },
        },
    };
}
