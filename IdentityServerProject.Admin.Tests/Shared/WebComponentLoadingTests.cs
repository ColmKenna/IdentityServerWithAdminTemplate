using System.Net;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;

namespace IdentityServerProject.Admin.Tests.Shared;

/// <summary>
///     The admin layout loads a web component module only when the page says it needs one, so
///     a page that uses a component without declaring it would silently fail to hydrate.
///     These guard that pairing.
/// </summary>
public class WebComponentLoadingTests
{
    private const string TableModule = "ck-responsive-table-webcomponent/index.esm.js";
    private const string TabsModule = "ck-tabs-webcomponent/index.esm.js";

    private static DirectoryInfo PagesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !Directory.Exists(Path.Combine(directory.FullName, "IdentityServerProject", "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var pages = new DirectoryInfo(Path.Combine(
            directory!.FullName, "IdentityServerProject", "src", "IdentityServerProject", "Pages"));
        Assert.True(pages.Exists, $"Pages directory not found from {AppContext.BaseDirectory}");
        return pages;
    }

    public static TheoryData<string> AdminPageFiles()
    {
        var data = new TheoryData<string>();
        foreach (FileInfo file in PagesDirectory().GetFiles("*.cshtml", SearchOption.AllDirectories))
            data.Add(file.FullName);
        return data;
    }

    [Theory]
    [MemberData(nameof(AdminPageFiles))]
    public void EveryPageUsingAComponent_DeclaresIt(string path)
    {
        string markup = File.ReadAllText(path);

        // The layout's own conditional script tags name the modules; skip it.
        if (Path.GetFileName(path) == "_AdminLayout.cshtml") return;

        bool usesTable = Regex.IsMatch(markup, @"<ck-responsive-table\b");
        bool usesTabs = Regex.IsMatch(markup, @"<ck-tabs\b");
        bool declaresTable = markup.Contains("AdminComponent.ResponsiveTable", StringComparison.Ordinal);
        bool declaresTabs = markup.Contains("AdminComponent.Tabs", StringComparison.Ordinal);

        Assert.True(usesTable == declaresTable,
            $"{Path.GetFileName(path)}: uses <ck-responsive-table>={usesTable} but declares ResponsiveTable={declaresTable}");
        Assert.True(usesTabs == declaresTabs,
            $"{Path.GetFileName(path)}: uses <ck-tabs>={usesTabs} but declares Tabs={declaresTabs}");
    }

    private static async Task<(bool Table, bool Tabs, IDocument Document)> LoadAsync(string url)
    {
        using var factory = new AdminWebFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        IDocument document = await context.OpenAsync(request => request.Content(html));

        string[] scripts = document.QuerySelectorAll("script[src]")
            .Select(script => script.GetAttribute("src")!)
            .ToArray();

        return (scripts.Any(src => src.Contains(TableModule, StringComparison.Ordinal)),
            scripts.Any(src => src.Contains(TabsModule, StringComparison.Ordinal)),
            document);
    }

    [Theory]
    [InlineData("/Admin/Clients")]
    [InlineData("/Admin/Users")]
    [InlineData("/Admin/Keys")]
    public async Task TablePage_LoadsOnlyTheTableModule(string url)
    {
        (bool table, bool tabs, IDocument document) = await LoadAsync(url);

        Assert.NotNull(document.QuerySelector("ck-responsive-table"));
        Assert.True(table, $"{url} renders a responsive table but did not load its module");
        Assert.False(tabs, $"{url} loaded the tabs module it does not use");
    }

    [Theory]
    [InlineData("/Admin")]
    [InlineData("/Admin/Diagnostics")]
    [InlineData("/Admin/Clients/Create")]
    [InlineData("/Admin/Roles/Create")]
    public async Task PageUsingNeitherComponent_LoadsNeitherModule(string url)
    {
        (bool table, bool tabs, IDocument document) = await LoadAsync(url);

        Assert.Null(document.QuerySelector("ck-responsive-table"));
        Assert.Null(document.QuerySelector("ck-tabs"));
        Assert.False(table, $"{url} loaded the table module it does not use");
        Assert.False(tabs, $"{url} loaded the tabs module it does not use");
    }

    [Fact]
    public async Task ScriptOrder_KeepsModulesAheadOfThePageInitialiser()
    {
        using var factory = new AdminWebFactory();
        string html = await factory.CreateClient().GetStringAsync("/Admin/Clients");

        int module = html.IndexOf(TableModule, StringComparison.Ordinal);
        int adminJs = html.IndexOf("/js/admin.js", StringComparison.Ordinal);

        Assert.True(module >= 0 && adminJs > module,
            "the component module must still be emitted before admin.js");
    }
}
