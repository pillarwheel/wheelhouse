using Microsoft.EntityFrameworkCore;
using WheelHouse.Core.Prompts;
using WheelHouse.Infrastructure.Persistence;
using WheelHouse.Infrastructure.Prompts;
using Xunit;

namespace WheelHouse.Tests;

public class PromptRenderingTests
{
    [Fact]
    public void Extracts_Distinct_Placeholders_In_Order()
    {
        var body = "Hi {{PROJECT_NAME}}, the {{ROLE_TITLE}} works on {{PROJECT_NAME}}.";
        var result = PromptRendering.ExtractPlaceholders(body);
        Assert.Equal(new[] { "PROJECT_NAME", "ROLE_TITLE" }, result);
    }

    [Fact]
    public void Renders_Supplied_Values()
    {
        var body = "You are the {{ROLE}} for {{PROJECT}}.";
        var values = new Dictionary<string, string?> { ["ROLE"] = "Lead Engineer", ["PROJECT"] = "WheelHouse" };
        Assert.Equal("You are the Lead Engineer for WheelHouse.", PromptRendering.Render(body, values));
    }

    [Fact]
    public void Leaves_Unfilled_Placeholders_Intact()
    {
        var body = "{{A}} and {{B}}";
        var values = new Dictionary<string, string?> { ["A"] = "first", ["B"] = "  " };
        Assert.Equal("first and {{B}}", PromptRendering.Render(body, values));
    }

    [Fact]
    public void Tolerates_Whitespace_In_Tokens()
    {
        Assert.Equal(new[] { "X" }, PromptRendering.ExtractPlaceholders("{{ X }}"));
        Assert.Equal("y", PromptRendering.Render("{{ X }}", new Dictionary<string, string?> { ["X"] = "y" }));
    }
}

public class PromptTemplateServiceTests
{
    private static WheelHouseDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<WheelHouseDbContext>()
            .UseSqlite($"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;
        var db = new WheelHouseDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task Seeds_All_BuiltIns_And_Is_Idempotent()
    {
        using var db = NewDb();
        var svc = new PromptTemplateService(db);

        var firstRun = await svc.SeedBuiltInsAsync();
        var secondRun = await svc.SeedBuiltInsAsync();

        Assert.Equal(BuiltInPromptTemplates.All.Count, firstRun);
        Assert.Equal(0, secondRun); // nothing added the second time
        Assert.Equal(BuiltInPromptTemplates.All.Count, await db.PromptTemplates.CountAsync());
    }

    [Fact]
    public async Task Saves_And_Deletes_Custom_Template()
    {
        using var db = NewDb();
        var svc = new PromptTemplateService(db);

        var saved = await svc.SaveAsync(new Core.Models.PromptTemplate
        {
            Name = "My Template", Body = "Do {{THING}}", Category = "Custom"
        });
        Assert.True(saved.Id > 0);

        await svc.DeleteAsync(saved.Id);
        Assert.Null(await svc.GetAsync(saved.Id));
    }
}
